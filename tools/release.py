"""Release packaging.

  python tools/release.py zip [--dirty]
      Builds the plugin (nothing is copied into the game) and writes dist/DD2Estate-<version>.zip: the DD2Estate
      folder a player copies into BepInEx/plugins, holding the DLL, the tracked files of assets/, LICENSE.txt,
      README.md and CHANGELOG.md. No .pdb. Refuses while src/ or assets/ have uncommitted changes, so that the
      archive is what the commit says (--dirty packs anyway).

  python tools/release.py snapshot --author "Name <address>" --message "..." [--from dev] [--branch AI-Slop]
      Makes the public branch: ONE commit that holds the tree of --from without the paths in PRIVATE and has no
      history behind it. A later run adds one commit on top of the branch. The working tree and the index are
      not touched, nothing is pushed.

The version is the csproj's <Version>.
"""
import os
import re
import subprocess
import sys
import tempfile
import time
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CSPROJ = os.path.join(ROOT, "src", "DD2Estate", "DD2Estate.csproj")
DLL = os.path.join(ROOT, "src", "DD2Estate", "bin", "Release", "DD2Estate.dll")
FOLDER = "DD2Estate"
BESIDE = ["LICENSE.txt", "README.md", "CHANGELOG.md"]
# what stays out of the public branch: the working journal and the agents' own instructions
PRIVATE = ["MODLOG.md", ".claude"]


def git(*args, env=None, check=True):
    r = subprocess.run(["git", "-C", ROOT] + list(args), capture_output=True, text=True, encoding="utf-8", env=env)
    if check and r.returncode != 0:
        sys.exit("git %s: %s" % (" ".join(args), r.stderr.strip() or r.stdout.strip()))
    return r.stdout.strip()


def version():
    with open(CSPROJ, encoding="utf-8") as f:
        found = re.search(r"<Version>\s*([^<\s]+)\s*</Version>", f.read())
    if not found:
        sys.exit("no <Version> in " + CSPROJ)
    return found.group(1)


def make_zip(dirty):
    changed = git("status", "--porcelain", "--", "src", "assets", *BESIDE)
    if changed and not dirty:
        sys.exit("uncommitted changes (commit them, or pass --dirty):\n" + changed)
    began = time.time()
    r = subprocess.run(["dotnet", "build", CSPROJ, "-c", "Release", "-nologo", "-v", "q", "-p:NoDeploy=true"], capture_output=True, text=True)
    if r.returncode != 0:
        sys.exit("the build failed:\n" + "\n".join(l for l in (r.stdout + r.stderr).splitlines() if "error" in l))
    if not os.path.exists(DLL) or os.path.getmtime(DLL) < began - 600:
        sys.exit("no fresh " + DLL)
    files = [(DLL, FOLDER + "/DD2Estate.dll")]
    for path in git("ls-files", "--", "assets").splitlines():
        files.append((os.path.join(ROOT, path), FOLDER + "/" + path))
    for name in BESIDE:
        files.append((os.path.join(ROOT, name), FOLDER + "/" + name))
    missing = [src for src, _ in files if not os.path.exists(src)]
    if missing:
        sys.exit("missing: " + ", ".join(missing))
    out_dir = os.path.join(ROOT, "dist")
    os.makedirs(out_dir, exist_ok=True)
    out = os.path.join(out_dir, "%s-%s.zip" % (FOLDER, version()))
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for src, name in files:
            z.write(src, name)
    with zipfile.ZipFile(out) as z:
        for info in z.infolist():
            print("%10d  %s" % (info.file_size, info.filename))
    print("%s  (%.1f MB, commit %s%s)" % (out, os.path.getsize(out) / 1048576.0, git("rev-parse", "--short", "HEAD"), ", with uncommitted changes" if changed else ""))


def option(name, default=None):
    if name in sys.argv:
        at = sys.argv.index(name)
        if at + 1 < len(sys.argv):
            return sys.argv[at + 1]
    return default


def make_snapshot():
    author, message = option("--author"), option("--message")
    source, branch = option("--from", "dev"), option("--branch", "AI-Slop")
    who = re.match(r"^\s*(.+?)\s*<([^<>]+)>\s*$", author or "")
    if not who or not message:
        sys.exit('snapshot needs --author "Name <address>" and --message "..."')
    if git("rev-parse", "--abbrev-ref", "HEAD") == branch:
        sys.exit("the branch %s is checked out: switch to another one first" % branch)
    with tempfile.TemporaryDirectory() as scratch:
        env = dict(os.environ, GIT_INDEX_FILE=os.path.join(scratch, "index"),
                   GIT_AUTHOR_NAME=who.group(1), GIT_AUTHOR_EMAIL=who.group(2),
                   GIT_COMMITTER_NAME=who.group(1), GIT_COMMITTER_EMAIL=who.group(2))
        git("read-tree", source, env=env)
        git("rm", "-r", "--cached", "-q", "--ignore-unmatch", "--", *PRIVATE, env=env)
        tree = git("write-tree", env=env)
        parent = git("rev-parse", "--verify", "-q", "refs/heads/" + branch, check=False)
        if parent and git("rev-parse", parent + "^{tree}") == tree:
            sys.exit("%s already holds this tree (%s)" % (branch, parent[:7]))
        commit = git("commit-tree", tree, *(["-p", parent] if parent else []), "-m", message, env=env)
    git("update-ref", "refs/heads/" + branch, commit)
    names = git("ls-tree", "-r", "--name-only", commit).splitlines()
    print("%s -> %s: %d files from %s (%s), %s" % (branch, commit[:7], len(names), source, git("rev-parse", "--short", source),
                                                    "on top of " + parent[:7] if parent else "no history"))
    print("author:", git("log", "-1", "--format=%an <%ae>", commit))
    left = [n for n in names if any(n == p or n.startswith(p + "/") for p in PRIVATE)]
    print("private paths in it:", left or "none")


if __name__ == "__main__":
    what = sys.argv[1] if len(sys.argv) > 1 else ""
    if what == "zip":
        make_zip("--dirty" in sys.argv)
    elif what == "snapshot":
        make_snapshot()
    else:
        sys.exit(__doc__)
