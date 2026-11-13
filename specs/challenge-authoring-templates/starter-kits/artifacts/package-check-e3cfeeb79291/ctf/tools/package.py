import argparse
import hashlib
import tarfile
import zipfile
from pathlib import Path


MODES = ("ctf", "awd", "awdp", "koh")
TOOLS = Path(__file__).resolve().parent
ROOT = TOOLS.parent
IGNORED = {"dist", "artifacts", "__pycache__", ".git"}


def kit_root(mode):
    path = ROOT if ROOT.name == mode else ROOT / mode
    if not (path / "NOCTF-DELIVERY.md").is_file():
        raise FileNotFoundError("Missing kit: " + mode)
    return path


def files(directory):
    for path in sorted(directory.rglob("*")):
        relative = path.relative_to(directory)
        if any(part in IGNORED for part in relative.parts):
            continue
        if path.is_symlink() or not (path.is_file() or path.is_dir()):
            raise ValueError("Archives require ordinary files and directories: " + str(relative))
        if path.is_file():
            if path.name.startswith(".env") or path.suffix in {".pem", ".key", ".pfx"}:
                raise ValueError("Credential file cannot be packaged: " + str(relative))
            yield path, relative


def add_file(archive, path, name):
    info = archive.gettarinfo(str(path), arcname=str(name).replace("\\", "/"))
    info.uid = info.gid = 0
    info.uname = info.gname = ""
    info.mode = 0o755 if path.suffix == ".sh" else 0o644
    info.mtime = 0
    with path.open("rb") as stream:
        archive.addfile(info, stream)


def write_archive(destination, entries):
    with tarfile.open(destination, "w:gz", format=tarfile.PAX_FORMAT) as archive:
        for path, name in entries:
            add_file(archive, path, name)
    with tarfile.open(destination, "r:gz") as archive:
        members = archive.getmembers()
        if not members or any(not member.isfile() for member in members):
            raise ValueError("Unexpected archive entries")


def package(mode, output):
    root = kit_root(mode)
    staff = output / ("noctf-" + mode + "-authoring-kit.tar.gz")
    entries = [(path, Path(mode) / relative) for path, relative in files(root)
               if relative.parts[0] != "tools"]
    entries.extend((path, Path(mode) / "tools" / relative) for path, relative in files(TOOLS))
    specs = ROOT.parent.parent
    if (specs / "challenge-authoring.md").is_file():
        entries.append((specs / "challenge-authoring.md", Path(mode) / "AUTHORING.md"))
        entries.append((ROOT.parent / (mode + "-challenge-template.md"), Path(mode) / "CONTRACT.md"))
    write_archive(staff, entries)
    attachments = output / ("noctf-" + mode + "-attachments.tar.gz")
    write_archive(attachments, files(root / "attachment"))
    produced = [staff, attachments]
    if mode == "awdp":
        for case in sorted((root / "private/fixes").iterdir()):
            if not case.is_dir() or case.is_symlink():
                raise ValueError("Invalid Fix sample")
            destination = output / ("noctf-awdp-fix-" + case.name + ".tar.gz")
            write_archive(destination, files(case))
            with tarfile.open(destination) as archive:
                if "fix.sh" not in archive.getnames():
                    raise ValueError("Fix entrypoint must be at archive root")
            produced.append(destination)
    return produced


def bundle(destination, produced, checksum):
    destination.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(destination, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for path in [*produced, checksum]:
            archive.write(path, path.name)
        specifications = ROOT.parent.parent
        guide = specifications / "challenge-authoring.md"
        if guide.is_file():
            archive.write(guide, "出题与部署.md")
            for mode in MODES:
                contract = ROOT.parent / (mode + "-challenge-template.md")
                archive.write(contract, mode + "-出题契约.md")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--mode", choices=(*MODES, "all"), default="all")
    parser.add_argument("--output", type=Path, default=ROOT / "dist")
    parser.add_argument("--bundle", type=Path)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    modes = MODES if args.mode == "all" else (args.mode,)
    produced = [path for mode in modes for path in package(mode, args.output)]
    lines = [hashlib.sha256(path.read_bytes()).hexdigest() + "  " + path.name for path in produced]
    checksum = args.output / "SHA256SUMS"
    checksum.write_text("\n".join(lines) + "\n", encoding="utf-8")
    if args.bundle is not None:
        if args.mode != "all":
            raise ValueError("The complete bundle requires --mode all")
        bundle(args.bundle, produced, checksum)
        print(args.bundle)
    for path in produced:
        print(path)


if __name__ == "__main__":
    main()
