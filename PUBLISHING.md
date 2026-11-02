# Maintaining this repository

Repository: <https://github.com/Zolotushka1/-translation-workshop-2dark>

Use this prepared folder as the repository root. Do not upload the entire game installation or research workspace. The existing initial MIT license commit is retained in the history.

Before publishing changes, review `git status`, `git diff --cached --stat` and `git ls-files`. Personal translations, notes, saves, runtime copies and game binaries must not be tracked. Do not force-add ignored files.

```powershell
git add README.md workshop scripts
git diff --cached --stat
git commit -m "Describe the change"
git push origin main
```

The software license covers the workshop's original code and documentation only. See `LICENSE_SCOPE.md` and `THIRD_PARTY_NOTICES.md` for third-party assets.

The locally prepared source ZIP is a snapshot, not the development repository. Make future changes in the Git working folder.
