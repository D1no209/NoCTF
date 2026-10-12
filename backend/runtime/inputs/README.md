This directory is the Docker build-context boundary for verified Satori packages.

`runtime.py image` creates an isolated tracked-source archive and copies the verified
manifest, approved runtime/apphost packages and licenses here in that temporary
context. Runtime binaries are never added to the source checkout or Git.

The official build needs no runtime inputs. Selecting Satori without a complete,
matching manifest fails; it never falls back to the installed runtime.
