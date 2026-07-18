# NoCTF QQBot agent

This is a separate, outbound-only NoneBot plugin for the Milky adapter used by the reference QQBOT deployment. It registers no incoming-message matcher and does not modify any existing QQBOT plugin.

1. Install `requirements.txt` in the BOT image/environment.
2. Generate an ECDSA P-256 key with `python generate_agent_key.py /secure/agent.pem`.
3. In NoCTF global QQBot settings, create an agent and paste only the printed public key. Keep the private key on the BOT host.
4. Copy or mount the `noctf_broadcast` directory into the BOT's NoneBot plugin directory and configure the variables in `.env.example`.
5. Start the BOT, authorize a synchronized group in NoCTF, then bind that group in the competition QQBot page.

For a designated test deployment, keep both the local allowlist and platform authorization restricted to one operator-approved group supplied outside version control. Use HTTPS with a valid certificate; the agent intentionally rejects HTTP URLs and redirects.

The SQLite journal prevents automatic resend after an ambiguous process or network failure. A delivery left in `sending` is reported as `delivery_outcome_unknown` and requires an audited manual retry decision in NoCTF.
