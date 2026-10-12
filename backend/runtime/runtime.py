#!/usr/bin/env python3
"""Pinned managed-runtime builds. Never installs a global SDK or patches DLLs in place."""
import argparse
import hashlib
import json
import os
import pathlib
import platform
import re
import shutil
import subprocess
import sys
import tarfile
import tempfile
import time
import uuid
import xml.etree.ElementTree as ET
import zipfile

ROOT = pathlib.Path(__file__).resolve().parent
BACKEND = ROOT.parent
LOCK = json.loads((ROOT / "runtime-lock.json").read_text(encoding="utf-8"))
PROJECTS = {"host": BACKEND / "src/NoCTF.Host/NoCTF.Host.csproj",
            "tests": BACKEND / "tests/NoCTF.Tests/NoCTF.Tests.csproj",
            "probe": ROOT / "probe/NoCTF.RuntimeProbe.csproj",
            "benchmarks": BACKEND / "benchmarks/NoCTF.Benchmarks/NoCTF.Benchmarks.csproj"}


def run(args, cwd=None, env=None, capture=False):
    return subprocess.run([str(arg) for arg in args], cwd=cwd, env=env, check=True,
                          text=True, encoding="utf-8", errors="replace",
                          stdout=subprocess.PIPE if capture else None).stdout


def digest(path):
    value = hashlib.sha256()
    with pathlib.Path(path).open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            value.update(chunk)
    return value.hexdigest()


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")
    temporary.replace(path)


def native_rid():
    if platform.machine().lower() not in ("amd64", "x86_64"):
        raise ValueError("Only native x64 build hosts are supported")
    return "win-x64" if sys.platform == "win32" else "linux-x64" if sys.platform == "linux" else "unsupported"


def audit_source(source):
    source = source.resolve()
    expected = LOCK["source"]
    head = run(["git", "rev-parse", "HEAD"], source, capture=True).strip()
    if head != expected["commit"]:
        raise ValueError("Satori source HEAD differs from runtime-lock.json")
    run(["git", "diff", "--exit-code", "HEAD", "--"], source, capture=True)
    run(["git", "merge-base", "--is-ancestor", expected["upstreamCommit"], head], source)
    changed = run(["git", "diff", "--name-only", expected["upstreamCommit"], head], source, capture=True).splitlines()
    return {"commit": head, "upstreamCommit": expected["upstreamCommit"], "upstreamTag": expected["upstreamTag"],
            "upstreamIsAncestor": True, "changedPaths": changed}


def fetch_source(args):
    source = args.source.resolve()
    if not source.exists():
        source.parent.mkdir(parents=True, exist_ok=True)
        run(["git", "-c", "core.longpaths=true", "clone", "--filter=blob:none", "--no-checkout",
             "--depth=256", "--single-branch", "--branch", "10.0-based", LOCK["source"]["repository"], source])
    remote = run(["git", "remote", "get-url", "origin"], source, capture=True).strip()
    if remote != LOCK["source"]["repository"]:
        raise ValueError("Refusing to modify a checkout with a different source repository")
    if run(["git", "status", "--porcelain", "--untracked-files=no"], source, capture=True).strip():
        # A no-checkout clone has an empty index; ordinary edited sources are never reset.
        if run(["git", "ls-files"], source, capture=True).strip():
            raise ValueError("Source checkout has modifications; refusing to overwrite them")
    run(["git", "-c", "core.longpaths=true", "fetch", "--filter=blob:none", "origin", LOCK["source"]["commit"]], source)
    run(["git", "fetch", "--filter=blob:none", "--depth=1", LOCK["source"]["upstreamRepository"],
         "refs/tags/" + LOCK["source"]["upstreamTag"] + ":refs/tags/noctf-security-baseline"], source)
    for depth in (256, 1024, 4096):
        result = subprocess.run(["git", "merge-base", "--is-ancestor", LOCK["source"]["upstreamCommit"], LOCK["source"]["commit"]], cwd=source)
        if result.returncode == 0:
            break
        run(["git", "fetch", "--filter=blob:none", "--deepen=" + str(depth), "origin", "10.0-based"], source)
    run(["git", "-c", "core.longpaths=true", "checkout", "--detach", LOCK["source"]["commit"]], source)
    report = audit_source(source)
    print(json.dumps({key: report[key] for key in ("commit", "upstreamCommit", "upstreamIsAncestor")}))


def package_identity(path):
    with zipfile.ZipFile(path) as archive:
        nuspecs = [name for name in archive.namelist() if name.endswith(".nuspec")]
        if len(nuspecs) != 1:
            raise ValueError("Package must contain exactly one nuspec")
        document = ET.fromstring(archive.read(nuspecs[0]))
        metadata = next(item for item in document if item.tag.split("}")[-1] == "metadata")
        values = {item.tag.split("}")[-1]: item.text for item in metadata}
        return values["id"], values["version"]


def required_ids(rid):
    return {"Microsoft.NETCore.App.Runtime." + rid, "Microsoft.NETCore.App.Host." + rid}


def verify_manifest(directory, rid):
    directory = directory.resolve()
    manifest = json.loads((directory / "manifest.json").read_text(encoding="utf-8"))
    if manifest["rid"] != rid or manifest["source"]["commit"] != LOCK["source"]["commit"]:
        raise ValueError("Runtime manifest RID or commit mismatch")
    if manifest.get("productVersion") != LOCK["source"]["productVersion"] or not re.fullmatch(
            re.escape(LOCK["source"]["productVersion"]) + r"(?:-[0-9A-Za-z.-]+)?", manifest["packageVersion"]):
        raise ValueError("Runtime package/product version does not match the locked .NET release")
    if manifest["source"]["upstreamCommit"] != LOCK["source"]["upstreamCommit"] or manifest["source"].get("upstreamIsAncestor") is not True:
        raise ValueError("Runtime manifest has no matching upstream security baseline")
    packages = manifest["packages"]
    if {item["id"] for item in packages} != required_ids(rid) or len(packages) != 2:
        raise ValueError("Runtime manifest must contain exactly the two approved runtime/apphost packages")
    for item in packages:
        path = (directory / item["file"]).resolve()
        if not path.is_relative_to(directory) or digest(path) != item["sha256"]:
            raise ValueError("Runtime package path or checksum is invalid")
        if package_identity(path) != (item["id"], manifest["packageVersion"]):
            raise ValueError("Runtime package identity differs from its manifest")
    return manifest


def verify_published(directory, runtime_dir, rid):
    manifest = verify_manifest(runtime_dir, rid)
    files = ("coreclr.dll", "clrjit.dll", "System.Private.CoreLib.dll") if rid == "win-x64" else (
        "libcoreclr.so", "libclrjit.so", "System.Private.CoreLib.dll")
    package = next(item for item in manifest["packages"] if ".Runtime." in item["id"])
    with zipfile.ZipFile(runtime_dir / package["file"]) as archive:
        for name in files:
            entries = [item for item in archive.namelist() if item.rsplit("/", 1)[-1] == name]
            if len(entries) != 1 or hashlib.sha256(archive.read(entries[0])).hexdigest() != digest(directory / name):
                raise ValueError("Published runtime is not the verified Satori package: " + name)


def build_runtime(args):
    if native_rid() != args.rid:
        raise ValueError("Build Satori on the native target OS (use the Linux build container for linux-x64)")
    # Preserve a caller-supplied SUBST drive on Windows for native tool path limits.
    source = args.source.absolute()
    report = audit_source(source)
    output = args.output.resolve()
    if output.exists() and any(output.iterdir()):
        raise ValueError("Runtime output must be empty; existing immutable runtime artifacts are not overwritten")
    command = ([args.powershell or "pwsh", "-NoProfile", "-File", "eng/build.ps1"] if sys.platform == "win32" else ["bash", "build.sh"])
    command += ["-s", LOCK["buildSubsets"], "-c", LOCK["configuration"], "-arch", "x64"]
    if args.nuget_exe:
        command.append("/p:NuGetExeFile=" + str(args.nuget_exe.resolve()))
    started = time.time()
    env = dict(os.environ, DOTNET_CLI_UI_LANGUAGE="en", VSLANG="1033", DOTNET_CLI_TELEMETRY_OPTOUT="1")
    if sys.platform == "win32":
        vswhere = pathlib.Path(os.environ["ProgramFiles(x86)"]) / "Microsoft Visual Studio/Installer/vswhere.exe"
        studio = args.visual_studio or pathlib.Path(run([vswhere, "-latest", "-products", "*", "-requires",
            "Microsoft.VisualStudio.Component.VC.Tools.x86.x64", "-property", "installationPath"], capture=True).strip())
        developer = studio / "Common7/Tools/VsDevCmd.bat"
        if not developer.is_file():
            raise ValueError("A stable Visual Studio installation with C++ tools is required")
        # Capture the developer environment privately; never print inherited credentials.
        initialized = subprocess.run('cmd.exe /d /s /c ""' + str(developer) + '" -no_logo -arch=x64 -host_arch=x64 && set"',
            capture_output=True, check=True, text=True, encoding="utf-8", errors="replace", env=env).stdout
        for line in initialized.splitlines():
            if "=" in line and not line.startswith("="):
                key, value = line.split("=", 1)
                env[key.upper()] = value
        cmake_dir = studio / "Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin"
        ninja_dir = studio / "Common7/IDE/CommonExtensions/Microsoft/CMake/Ninja"
        env["PATH"] = str(cmake_dir) + os.pathsep + str(ninja_dir) + os.pathsep + env["PATH"]
    run(command, source, env)
    audit_source(source)
    packages = []
    for path in (source / "artifacts/packages/Release/Shipping").glob("*.nupkg"):
        if path.name.endswith(".symbols.nupkg"):
            continue
        name, version = package_identity(path)
        if name not in required_ids(args.rid):
            continue
        target = output / "packages" / path.name
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(path, target)
        packages.append({"id": name, "version": version, "file": "packages/" + path.name, "sha256": digest(target)})
    versions = {item["version"] for item in packages}
    if len(packages) != 2 or len(versions) != 1:
        raise ValueError("Shipping output did not produce the expected runtime/apphost package pair")
    symbols = []
    core = source / "artifacts/bin/coreclr" / ({"linux-x64": "linux.x64.Release", "win-x64": "windows.x64.Release"}[args.rid])
    for path in core.rglob("*") if core.exists() else []:
        if path.is_file() and path.suffix in (".pdb", ".dbg"):
            relative = pathlib.Path("symbols") / path.relative_to(core)
            target = output / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(path, target)
            symbols.append({"file": relative.as_posix(), "sha256": digest(target)})
    for name in ("LICENSE.TXT", "THIRD-PARTY-NOTICES.TXT", "PATENTS.TXT"):
        target = output / "licenses" / name
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source / name, target)
    manifest = {"format": 1, "rid": args.rid, "source": report, "productVersion": LOCK["source"]["productVersion"],
                "packageVersion": next(iter(versions)), "packages": packages, "symbols": symbols,
                "build": {"command": command, "host": platform.platform(), "python": platform.python_version(),
                          "cmake": run([shutil.which("cmake", path=env.get("PATH")) or "cmake", "--version"], env=env, capture=True).splitlines()[0],
                          "runtimeSdk": run([source / (".dotnet/dotnet.exe" if sys.platform == "win32" else ".dotnet/dotnet"), "--version"], source, env, capture=True).strip(),
                          "buildImage": os.environ.get("NOCTF_RUNTIME_BUILD_IMAGE"),
                          "visualStudioVersion": env.get("VSCMD_VER"),
                          "startedAtUnix": started, "completedAtUnix": time.time()}}
    write_json(output / "manifest.json", manifest)
    verify_manifest(output, args.rid)
    print("Verified runtime manifest: " + str(output / "manifest.json"))


def build_linux(args):
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    if any(output.iterdir()):
        raise ValueError("Linux runtime output must be empty")
    name = "noctf-satori-" + LOCK["source"]["commit"][:12]
    command = ["docker", "run", "--rm", "--name", name, "--cpus", str(args.cpus), "--memory", args.memory,
               "--mount", "type=volume,source=" + name + "-source,target=/src",
               "--mount", "type=bind,source=" + str(ROOT) + ",target=/noctf/backend/runtime,readonly",
               "--mount", "type=bind,source=" + str(output) + ",target=/out",
               "-e", "DOTNET_PROCESSOR_COUNT=" + str(args.cpus),
               "-e", "NOCTF_RUNTIME_BUILD_IMAGE=" + LOCK["linuxBuildImage"],
               "-e", "GIT_CONFIG_COUNT=1", "-e", "GIT_CONFIG_KEY_0=safe.directory", "-e", "GIT_CONFIG_VALUE_0=/src/source",
               LOCK["linuxBuildImage"], "python3", "/noctf/backend/runtime/runtime.py", "fetch-build",
               "--source", "/src/source", "--rid", "linux-x64", "--output", "/out"]
    run(command)


def fetch_build(args):
    fetch_source(args)
    build_runtime(args)


def nuget_config(path, runtime_dir, package_ids):
    root = ET.Element("configuration")
    sources = ET.SubElement(root, "packageSources")
    ET.SubElement(sources, "clear")
    if runtime_dir:
        ET.SubElement(sources, "add", key="satori", value=str(runtime_dir / "packages"))
    ET.SubElement(sources, "add", key="nuget.org", value="https://api.nuget.org/v3/index.json")
    mappings = ET.SubElement(root, "packageSourceMapping")
    if runtime_dir:
        feed = ET.SubElement(mappings, "packageSource", key="satori")
        for name in sorted(package_ids):
            ET.SubElement(feed, "package", pattern=name)
    feed = ET.SubElement(mappings, "packageSource", key="nuget.org")
    ET.SubElement(feed, "package", pattern="*")
    ET.ElementTree(root).write(path, encoding="utf-8", xml_declaration=True)


def publish(args):
    runtime_dir = args.runtime_dir.resolve() if args.runtime_dir else None
    manifest = verify_manifest(runtime_dir, args.rid) if args.flavor == "Satori" and runtime_dir else None
    if args.flavor == "Satori" and manifest is None:
        raise ValueError("--runtime-dir is required for Satori; no official fallback is allowed")
    project = PROJECTS[args.project]
    key = LOCK["source"]["commit"] if manifest else LOCK["officialRuntime"]
    cache = (BACKEND / "artifacts/managed-runtime" / args.flavor / key / args.rid).resolve()
    cache.mkdir(parents=True, exist_ok=True)
    config = cache / "NuGet.Config"
    nuget_config(config, runtime_dir, required_ids(args.rid))
    env = dict(os.environ, NUGET_PACKAGES=str(cache / "nuget"), DOTNET_CLI_TELEMETRY_OPTOUT="1")
    command = ["dotnet", "publish", project, "--configuration", "Release", "--runtime", args.rid, "--self-contained", "true",
               "--output", args.output.resolve(), "-p:NoCtfRuntimeFlavor=" + args.flavor,
               "-p:UseArtifactsOutput=true", "-p:ArtifactsPath=" + str(cache / "build"),
               "-p:RestoreConfigFile=" + str(config), "-p:PublishReadyToRun=false", "-p:PublishTrimmed=false",
               "-p:BuildSpaOnPublish=" + ("true" if args.spa else "false")]
    if manifest:
        command += ["-p:NoCtfSatoriPackageVersion=" + manifest["packageVersion"],
                    "-p:NoCtfSatoriVerifiedManifest=" + str(runtime_dir / "manifest.json")]
    else:
        command.append("-p:RuntimeFrameworkVersion=" + LOCK["officialRuntime"])
    run(command, BACKEND, env)
    output = args.output.resolve()
    dll = "coreclr.dll" if args.rid == "win-x64" else "libcoreclr.so"
    if manifest:
        verify_published(output, runtime_dir, args.rid)
    revision = args.application_revision or run(["git", "rev-parse", "HEAD"], BACKEND, capture=True).strip()
    if not re.fullmatch(r"[0-9a-f]{40}", revision):
        raise ValueError("The application source revision must be a full Git commit SHA")
    dirty = False if args.application_revision else bool(run(["git", "diff", "HEAD", "--name-only"], BACKEND, capture=True).strip()
        or run(["git", "ls-files", "--others", "--exclude-standard", "--", "src", "runtime", "tests"], BACKEND, capture=True).strip())
    write_json(output / "noctf-runtime-manifest.json", {"flavor": args.flavor, "rid": args.rid,
               "sourceCommit": LOCK["source"]["commit"] if manifest else None, "applicationRevision": revision,
               "applicationDirty": dirty, "runtimeVersion": manifest["packageVersion"] if manifest else LOCK["officialRuntime"],
               "aspNetCoreVersion": LOCK["aspNetCoreRuntime"], "coreclrSha256": digest(output / dll),
               "coreLibSha256": digest(output / "System.Private.CoreLib.dll")})
    if manifest:
        shutil.copytree(runtime_dir / "licenses", output / "runtime-licenses", dirs_exist_ok=True)
    if args.rid == native_rid() and args.project in ("host", "probe", "benchmarks"):
        exe = project.stem + (".exe" if args.rid == "win-x64" else "")
        run([output / exe, "--runtime-info"], BACKEND, env)


def image(args):
    if args.flavor == "Satori":
        if args.runtime_dir is None:
            raise ValueError("Satori images require --runtime-dir")
        verify_manifest(args.runtime_dir, "linux-x64")
    repository = BACKEND.parent
    if run(["git", "diff", "HEAD", "--name-only"], repository, capture=True).strip():
        raise ValueError("Image publication requires an atomic committed source checkpoint")
    revision = run(["git", "rev-parse", "HEAD"], repository, capture=True).strip()
    parent = BACKEND / "artifacts/managed-runtime/contexts"
    parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix=revision[:12] + "-", dir=parent) as temporary:
        directory = pathlib.Path(temporary)
        archive_path = directory / "source.tar"
        run(["git", "archive", "--format=tar", "-o", archive_path, "HEAD"], repository)
        source = directory / "source"
        source.mkdir()
        with tarfile.open(archive_path) as archive:
            for item in archive.getmembers():
                target = (source / item.name).resolve()
                if not target.is_relative_to(source.resolve()) or item.issym() or item.islnk():
                    raise ValueError("Unexpected source archive path or link")
            archive.extractall(source, filter="data")
        if args.flavor == "Satori":
            inputs = source / "backend/runtime/inputs"
            runtime = args.runtime_dir.resolve()
            shutil.copyfile(runtime / "manifest.json", inputs / "manifest.json")
            for name in ("packages", "licenses"):
                shutil.copytree(runtime / name, inputs / name)
        run(["docker", "build", "--file", source / "backend/Dockerfile", "--target", "host",
             "--build-arg", "NOCTF_RUNTIME_FLAVOR=" + args.flavor,
             "--build-arg", "NOCTF_APPLICATION_REVISION=" + revision,
             "--label", "org.opencontainers.image.revision=" + revision,
             "--label", "io.noctf.runtime.flavor=" + args.flavor,
             "--tag", args.tag, source])


def start(args):
    manifest = json.loads((args.directory / "noctf-runtime-manifest.json").read_text(encoding="utf-8"))
    if manifest["flavor"] != args.flavor or manifest["rid"] != native_rid():
        raise ValueError("Published app does not match the requested runtime flavor and local OS")
    exe = args.directory.resolve() / ("NoCTF.Host.exe" if native_rid() == "win-x64" else "NoCTF.Host")
    run([exe, "--runtime-info"])
    run([exe] + args.arguments, args.directory.resolve())


def archive_publish(args):
    directory = args.directory.resolve()
    manifest = json.loads((directory / "noctf-runtime-manifest.json").read_text(encoding="utf-8"))
    if manifest["applicationDirty"]:
        raise ValueError("Release archives require a committed application source checkpoint")
    output = args.output.resolve()
    if output.is_relative_to(directory) or output.exists():
        raise ValueError("Use a new archive path outside the published directory")
    output.parent.mkdir(parents=True, exist_ok=True)
    paths = sorted(path for path in directory.rglob("*") if path.is_file())
    if any(path.is_symlink() for path in directory.rglob("*")):
        raise ValueError("Published artifacts must not contain symbolic links")
    if manifest["rid"] == "win-x64" and output.suffix == ".zip":
        with zipfile.ZipFile(output, "x", compression=zipfile.ZIP_DEFLATED) as archive:
            for path in paths:
                archive.write(path, path.relative_to(directory).as_posix())
    elif manifest["rid"] == "linux-x64" and output.name.endswith(".tar.gz"):
        with tarfile.open(output, "x:gz") as archive:
            for path in paths:
                archive.add(path, arcname=path.relative_to(directory).as_posix())
    else:
        raise ValueError("Use .zip for Windows and .tar.gz for Linux")
    checksum = digest(output)
    output.with_name(output.name + ".sha256").write_text(checksum + "  " + output.name + "\n", encoding="utf-8")
    print(json.dumps({"archive": str(output), "sha256": checksum, "runtime": manifest}))


def test(args):
    directory = args.directory.resolve()
    manifest = json.loads((directory / "noctf-runtime-manifest.json").read_text(encoding="utf-8"))
    if manifest["rid"] != native_rid() or manifest["flavor"] != args.flavor:
        raise ValueError("Test executable is for a different platform or runtime flavor")
    executable = directory / ("NoCTF.Tests.exe" if native_rid() == "win-x64" else "NoCTF.Tests")
    log = args.log.resolve()
    log.parent.mkdir(parents=True, exist_ok=True)
    started = time.time()
    command = [str(executable), "--treenode-filter", args.filter, "--maximum-parallel-tests", str(args.parallel)]
    identity_path = log.with_name(log.stem + "-" + uuid.uuid4().hex + ".identity.json")
    with log.open("w", encoding="utf-8") as stream:
        result = subprocess.run(command, cwd=BACKEND, stdout=stream, stderr=subprocess.STDOUT,
                                env=dict(os.environ, NOCTF_REQUIRE_DOCKER_INTEGRATION="true",
                                         NOCTF_TEST_RUNTIME_RECEIPT=str(identity_path)))
    identities = [json.loads(identity_path.read_text(encoding="utf-8"))] if identity_path.exists() else []
    valid = bool(identities) and all(item["actualFlavor"] == args.flavor and item["expectedFlavor"] == args.flavor for item in identities)
    record = {"format": 1, "kind": "functional-tests", "applicationRevision": manifest["applicationRevision"],
              "applicationDirty": manifest["applicationDirty"], "rid": manifest["rid"], "flavor": args.flavor,
              "runtimeIdentityVerified": valid, "filter": args.filter, "maximumParallelTests": args.parallel, "exitCode": result.returncode,
              "elapsedSeconds": time.time() - started, "logSha256": digest(log), "log": log.name,
              "runtimeIdentity": identities[0] if identities else None,
              "status": "passed" if valid and result.returncode == 0 else "failed"}
    write_json(log.with_suffix(".json"), record)
    print(json.dumps(record))
    if record["status"] != "passed":
        raise SystemExit(1)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    fetch = commands.add_parser("fetch")
    fetch.add_argument("--source", required=True, type=pathlib.Path)
    fetch.set_defaults(action=fetch_source)
    for name, action in (("build", build_runtime), ("fetch-build", fetch_build)):
        build = commands.add_parser(name)
        build.add_argument("--source", required=True, type=pathlib.Path)
        build.add_argument("--output", required=True, type=pathlib.Path)
        build.add_argument("--rid", required=True, choices=LOCK["runtimeIdentifiers"])
        build.add_argument("--powershell", help="PowerShell 7 executable for the upstream Windows build entry point")
        build.add_argument("--visual-studio", type=pathlib.Path, help="Stable Visual Studio installation (defaults to vswhere without prerelease)")
        build.add_argument("--nuget-exe", type=pathlib.Path, help="Pre-acquired upstream NuGet 3.5.0 tool for Windows installer packaging")
        build.set_defaults(action=action)
    linux = commands.add_parser("build-linux")
    linux.add_argument("--output", required=True, type=pathlib.Path)
    linux.add_argument("--cpus", type=int, default=8)
    linux.add_argument("--memory", default="12g")
    linux.set_defaults(action=build_linux)
    verify = commands.add_parser("verify")
    verify.add_argument("--runtime-dir", type=pathlib.Path, required=True)
    verify.add_argument("--rid", choices=LOCK["runtimeIdentifiers"], required=True)
    verify.set_defaults(action=lambda args: print(json.dumps({"status": "verified", "packageVersion": verify_manifest(args.runtime_dir, args.rid)["packageVersion"]})))
    published = commands.add_parser("verify-published")
    published.add_argument("--runtime-dir", type=pathlib.Path, required=True)
    published.add_argument("--directory", type=pathlib.Path, required=True)
    published.add_argument("--rid", choices=LOCK["runtimeIdentifiers"], required=True)
    published.set_defaults(action=lambda args: verify_published(args.directory, args.runtime_dir, args.rid))
    pub = commands.add_parser("publish")
    pub.add_argument("--flavor", choices=["Official", "Satori"], default=LOCK["defaultFlavor"])
    pub.add_argument("--rid", choices=LOCK["runtimeIdentifiers"], required=True)
    pub.add_argument("--runtime-dir", type=pathlib.Path)
    pub.add_argument("--project", choices=PROJECTS, default="host")
    pub.add_argument("--output", type=pathlib.Path, required=True)
    pub.add_argument("--spa", action="store_true")
    pub.add_argument("--application-revision", help="Exact revision of an exported source archive (container builds)")
    pub.set_defaults(action=publish)
    img = commands.add_parser("image")
    img.add_argument("--flavor", choices=["Official", "Satori"], default=LOCK["defaultFlavor"])
    img.add_argument("--runtime-dir", type=pathlib.Path)
    img.add_argument("--tag", required=True)
    img.set_defaults(action=image)
    launch = commands.add_parser("start")
    launch.add_argument("--flavor", choices=["Official", "Satori"], default="Satori")
    launch.add_argument("--directory", type=pathlib.Path, required=True)
    launch.add_argument("arguments", nargs=argparse.REMAINDER)
    launch.set_defaults(action=start)
    archive = commands.add_parser("archive")
    archive.add_argument("--directory", type=pathlib.Path, required=True)
    archive.add_argument("--output", type=pathlib.Path, required=True)
    archive.set_defaults(action=archive_publish)
    tests = commands.add_parser("test")
    tests.add_argument("--directory", type=pathlib.Path, required=True)
    tests.add_argument("--flavor", choices=["Official", "Satori"], required=True)
    tests.add_argument("--filter", default="/*/*/*/*")
    tests.add_argument("--parallel", type=int, default=4)
    tests.add_argument("--log", type=pathlib.Path, required=True)
    tests.set_defaults(action=test)
    args = parser.parse_args()
    args.action(args)


if __name__ == "__main__":
    main()
