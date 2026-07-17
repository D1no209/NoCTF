"""NoCTF outbound broadcast agent for NoneBot + Milky.

This plugin registers no message matcher and never handles incoming QQ messages.
"""

from __future__ import annotations

import asyncio
import base64
import hashlib
import json
import logging
import os
import secrets
import sqlite3
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Any
from urllib.parse import urlparse

import httpx
import nonebot
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import ec
from nonebot.adapters.milky import Message, MessageSegment
from nonebot.adapters.milky.exception import ActionFailed

logger = logging.getLogger("noctf.qqbot")


@dataclass(frozen=True)
class Settings:
    platform_url: str
    agent_id: str
    private_key_path: Path
    journal_path: Path
    group_allowlist: frozenset[int]
    request_timeout_seconds: float

    @classmethod
    def from_environment(cls) -> "Settings":
        platform_url = os.environ.get("NOCTF_PLATFORM_URL", "").rstrip("/")
        parsed = urlparse(platform_url)
        if parsed.scheme != "https" or not parsed.netloc or parsed.username or parsed.password:
            raise RuntimeError("NOCTF_PLATFORM_URL must be an HTTPS origin without credentials")
        agent_id = os.environ.get("NOCTF_QQBOT_AGENT_ID", "").strip()
        private_key_path = Path(os.environ.get("NOCTF_QQBOT_PRIVATE_KEY_PATH", "")).expanduser()
        if not agent_id or not private_key_path.is_file():
            raise RuntimeError("NOCTF_QQBOT_AGENT_ID and an existing private key path are required")
        allowlist_text = os.environ.get("NOCTF_QQBOT_GROUP_ALLOWLIST", "").strip()
        allowlist = frozenset(int(value.strip()) for value in allowlist_text.split(",") if value.strip())
        if not allowlist or any(value <= 0 for value in allowlist):
            raise RuntimeError("NOCTF_QQBOT_GROUP_ALLOWLIST must contain positive QQ group numbers")
        return cls(
            platform_url=platform_url,
            agent_id=agent_id,
            private_key_path=private_key_path,
            journal_path=Path(os.environ.get("NOCTF_QQBOT_JOURNAL_PATH", "data/noctf_qqbot.sqlite3")),
            group_allowlist=allowlist,
            request_timeout_seconds=float(os.environ.get("NOCTF_QQBOT_REQUEST_TIMEOUT_SECONDS", "35")),
        )


class Journal:
    def __init__(self, path: Path) -> None:
        self.path = path
        path.parent.mkdir(parents=True, exist_ok=True)
        with self._connect() as connection:
            connection.execute(
                """CREATE TABLE IF NOT EXISTS deliveries (
                    delivery_id TEXT PRIMARY KEY,
                    lease_token TEXT NOT NULL,
                    state TEXT NOT NULL CHECK (state IN ('sending', 'sent')),
                    message_sequence INTEGER,
                    remote_sent_at TEXT,
                    updated_at INTEGER NOT NULL
                )"""
            )

    def _connect(self) -> sqlite3.Connection:
        connection = sqlite3.connect(self.path, timeout=5)
        connection.execute("PRAGMA journal_mode=WAL")
        connection.execute("PRAGMA synchronous=FULL")
        return connection

    def get(self, delivery_id: str) -> tuple[str, str, int | None, str | None] | None:
        with self._connect() as connection:
            row = connection.execute(
                "SELECT state, lease_token, message_sequence, remote_sent_at FROM deliveries WHERE delivery_id = ?",
                (delivery_id,),
            ).fetchone()
        return tuple(row) if row else None  # type: ignore[return-value]

    def mark_sending(self, delivery_id: str, lease_token: str) -> None:
        with self._connect() as connection:
            connection.execute(
                "INSERT INTO deliveries(delivery_id, lease_token, state, updated_at) VALUES(?, ?, 'sending', ?) "
                "ON CONFLICT(delivery_id) DO UPDATE SET lease_token=excluded.lease_token, state='sending', updated_at=excluded.updated_at",
                (delivery_id, lease_token, int(time.time())),
            )

    def mark_sent(self, delivery_id: str, lease_token: str, sequence: int | None, sent_at: str | None) -> None:
        with self._connect() as connection:
            connection.execute(
                "UPDATE deliveries SET lease_token=?, state='sent', message_sequence=?, remote_sent_at=?, updated_at=? WHERE delivery_id=?",
                (lease_token, sequence, sent_at, int(time.time()), delivery_id),
            )

    def remove(self, delivery_id: str) -> None:
        with self._connect() as connection:
            connection.execute("DELETE FROM deliveries WHERE delivery_id=?", (delivery_id,))


class PlatformClient:
    def __init__(self, settings: Settings) -> None:
        self.settings = settings
        self.private_key = serialization.load_pem_private_key(
            settings.private_key_path.read_bytes(), password=None
        )
        if not isinstance(self.private_key, ec.EllipticCurvePrivateKey) or not isinstance(
            self.private_key.curve, ec.SECP256R1
        ):
            raise RuntimeError("The agent private key must use ECDSA P-256")
        self.client = httpx.AsyncClient(
            base_url=settings.platform_url,
            timeout=httpx.Timeout(settings.request_timeout_seconds),
            follow_redirects=False,
            limits=httpx.Limits(max_connections=4, max_keepalive_connections=2),
        )

    async def close(self) -> None:
        await self.client.aclose()

    async def post(self, path: str, payload: dict[str, Any]) -> dict[str, Any]:
        body = json.dumps(payload, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
        timestamp = str(int(time.time()))
        nonce = secrets.token_urlsafe(24)
        body_hash = hashlib.sha256(body).hexdigest()
        canonical = "\n".join(("NOCTF-QQBOT-V1", "POST", path, timestamp, nonce, body_hash)).encode()
        signature = self.private_key.sign(canonical, ec.ECDSA(hashes.SHA256()))
        response = await self.client.post(
            path,
            content=body,
            headers={
                "Content-Type": "application/json",
                "X-NoCTF-Agent-Id": self.settings.agent_id,
                "X-NoCTF-Timestamp": timestamp,
                "X-NoCTF-Nonce": nonce,
                "X-NoCTF-Signature": base64.b64encode(signature).decode("ascii"),
            },
        )
        response.raise_for_status()
        data = response.json()
        if not isinstance(data, dict):
            raise RuntimeError("NoCTF returned a non-object response")
        return data


class BroadcastAgent:
    def __init__(self, settings: Settings) -> None:
        self.settings = settings
        self.platform = PlatformClient(settings)
        self.journal = Journal(settings.journal_path)
        self.stop_event = asyncio.Event()

    async def run(self) -> None:
        heartbeat_at = 0.0
        group_sync_at = 0.0
        while not self.stop_event.is_set():
            try:
                now = time.monotonic()
                bot = self._milky_bot()
                if now >= heartbeat_at:
                    await self._heartbeat(bot)
                    heartbeat_at = now + 30
                if now >= group_sync_at:
                    await self._sync_groups(bot)
                    group_sync_at = now + 300
                lease = await self.platform.post(
                    "/api/integrations/qqbot/v1/deliveries/lease", {"waitSeconds": 25}
                )
                delivery = lease.get("delivery")
                if delivery:
                    await self._deliver(bot, delivery)
                else:
                    await asyncio.sleep(max(0.1, int(lease.get("retryAfterMilliseconds", 500)) / 1000))
            except asyncio.CancelledError:
                raise
            except Exception as exc:
                if str(exc) == "bot_offline" and time.monotonic() >= heartbeat_at:
                    try:
                        await self._offline_heartbeat()
                        heartbeat_at = time.monotonic() + 30
                    except Exception as heartbeat_exc:
                        logger.warning("NoCTF offline heartbeat failed: %s", _safe_error(heartbeat_exc))
                logger.warning("NoCTF broadcast loop temporarily unavailable: %s", _safe_error(exc))
                await asyncio.sleep(2)

    async def close(self) -> None:
        self.stop_event.set()
        await self.platform.close()

    @staticmethod
    def _milky_bot() -> Any:
        bots = [bot for bot in nonebot.get_bots().values() if bot.type == "Milky"]
        if not bots:
            raise RuntimeError("bot_offline")
        return bots[0]

    async def _heartbeat(self, bot: Any) -> None:
        login = _plain(await bot.call_api("get_login_info"))
        implementation = _plain(await bot.call_api("get_impl_info"))
        await self.platform.post(
            "/api/integrations/qqbot/v1/heartbeat",
            {
                "qqOnline": True,
                "botUin": _integer(login, "uin", "user_id"),
                "botNickname": _text(login, "nickname", "name"),
                "implementationName": _text(implementation, "impl_name", "implementation_name", "name"),
                "implementationVersion": _text(implementation, "impl_version", "implementation_version", "version"),
                "milkyVersion": _text(implementation, "milky_version"),
                "errorCode": None,
                "errorSummary": None,
            },
        )

    async def _offline_heartbeat(self) -> None:
        await self.platform.post(
            "/api/integrations/qqbot/v1/heartbeat",
            {
                "qqOnline": False,
                "botUin": None,
                "botNickname": None,
                "implementationName": "NoCTF outbound agent",
                "implementationVersion": None,
                "milkyVersion": None,
                "errorCode": "bot_offline",
                "errorSummary": "No connected Milky bot is available.",
            },
        )

    async def _sync_groups(self, bot: Any) -> None:
        raw = _plain(await bot.get_group_list(no_cache=False))
        items = raw if isinstance(raw, list) else raw.get("groups", []) if isinstance(raw, dict) else []
        groups: list[dict[str, Any]] = []
        for item in items:
            plain = _plain(item)
            group_id = _integer(plain, "group_id")
            if group_id in self.settings.group_allowlist:
                groups.append({"groupId": group_id, "groupName": _text(plain, "group_name", "name") or str(group_id)})
        await self.platform.post("/api/integrations/qqbot/v1/groups/sync", {"groups": groups})

    async def _deliver(self, bot: Any, delivery: dict[str, Any]) -> None:
        delivery_id = str(delivery["deliveryId"])
        lease_token = str(delivery["leaseToken"])
        group_id = int(delivery["groupId"])
        if group_id not in self.settings.group_allowlist:
            await self._fail(delivery_id, lease_token, "permission_denied", "group rejected by local allowlist")
            return
        previous = await asyncio.to_thread(self.journal.get, delivery_id)
        if previous and previous[0] == "sent":
            await self._ack(delivery_id, lease_token, previous[2], previous[3], "recovered from local sent journal")
            await asyncio.to_thread(self.journal.remove, delivery_id)
            return
        if previous and previous[0] == "sending":
            await self._fail(delivery_id, lease_token, "delivery_outcome_unknown", "previous send outcome is ambiguous")
            return
        try:
            segments = json.loads(str(delivery["segmentsJson"]))
            message = _build_message(segments)
        except (KeyError, TypeError, ValueError, json.JSONDecodeError) as exc:
            await self._fail(delivery_id, lease_token, "invalid_message", _safe_error(exc))
            return
        await asyncio.to_thread(self.journal.mark_sending, delivery_id, lease_token)
        try:
            result = _plain(await bot.call_api("send_group_message", group_id=group_id, message=message))
            sequence = _integer(result, "message_seq", "message_sequence")
            sent_at = _text(result, "time", "sent_at")
            await asyncio.to_thread(self.journal.mark_sent, delivery_id, lease_token, sequence, sent_at)
            await self._ack(delivery_id, lease_token, sequence, sent_at, "Milky accepted send_group_message")
            await asyncio.to_thread(self.journal.remove, delivery_id)
        except ActionFailed as exc:
            code = str(getattr(exc, "retcode", getattr(exc, "code", "")))
            mapped = "bot_offline" if code == "-403" else "invalid_message" if code == "-400" else "unknown"
            await asyncio.to_thread(self.journal.remove, delivery_id)
            await self._fail(delivery_id, lease_token, mapped, _safe_error(exc))
        except (httpx.TimeoutException, TimeoutError) as exc:
            await self._fail(delivery_id, lease_token, "delivery_outcome_unknown", _safe_error(exc))
        except Exception as exc:
            await self._fail(delivery_id, lease_token, "delivery_outcome_unknown", _safe_error(exc))

    async def _ack(self, delivery_id: str, lease_token: str, sequence: int | None, sent_at: str | None, summary: str) -> None:
        await self.platform.post("/api/integrations/qqbot/v1/deliveries/ack", {
            "deliveryId": delivery_id, "leaseToken": lease_token, "messageSequence": sequence,
            "remoteSentAt": sent_at, "safeRemoteSummary": summary,
        })

    async def _fail(self, delivery_id: str, lease_token: str, code: str, summary: str) -> None:
        await self.platform.post("/api/integrations/qqbot/v1/deliveries/fail", {
            "deliveryId": delivery_id, "leaseToken": lease_token,
            "errorCode": code, "safeSummary": summary[:500],
        })


def _build_message(segments: Any) -> Message:
    if not isinstance(segments, list) or not 1 <= len(segments) <= 4:
        raise ValueError("segment list invalid")
    message = Message()
    for segment in segments:
        if not isinstance(segment, dict) or set(segment) != {"type", "data"} or not isinstance(segment["data"], dict):
            raise ValueError("segment invalid")
        if segment["type"] == "text" and set(segment["data"]) == {"text"}:
            text = segment["data"]["text"]
            if not isinstance(text, str) or not text or len(text) > 4000:
                raise ValueError("text segment invalid")
            message.append(MessageSegment.text(text))
        elif segment["type"] == "mention_all" and not segment["data"]:
            message.append(MessageSegment("mention_all", {}))
        else:
            raise ValueError("segment type rejected")
    return message


def _plain(value: Any) -> Any:
    if hasattr(value, "model_dump"):
        return value.model_dump(mode="json")
    if isinstance(value, list):
        return [_plain(item) for item in value]
    return value


def _integer(value: Any, *keys: str) -> int | None:
    if not isinstance(value, dict):
        return None
    for key in keys:
        try:
            if value.get(key) is not None:
                return int(value[key])
        except (TypeError, ValueError):
            pass
    return None


def _text(value: Any, *keys: str) -> str | None:
    if not isinstance(value, dict):
        return None
    for key in keys:
        item = value.get(key)
        if item is not None:
            return str(item)[:160]
    return None


def _safe_error(exc: Exception) -> str:
    return f"{type(exc).__name__}: {str(exc)[:300]}".replace("\r", " ").replace("\n", " ")


driver = nonebot.get_driver()
_agent: BroadcastAgent | None = None
_task: asyncio.Task[None] | None = None


@driver.on_startup
async def _start() -> None:
    global _agent, _task
    _agent = BroadcastAgent(Settings.from_environment())
    _task = asyncio.create_task(_agent.run(), name="noctf-qqbot-broadcast")


@driver.on_shutdown
async def _stop() -> None:
    if _agent:
        await _agent.close()
    if _task:
        _task.cancel()
        await asyncio.gather(_task, return_exceptions=True)
