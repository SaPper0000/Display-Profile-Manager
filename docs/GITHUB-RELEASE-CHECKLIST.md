# GitHub Release Checklist

## Source repository

- [ ] Open the `.sln` in Visual Studio.
- [ ] Confirm the project loads without a missing-project warning.
- [ ] Select `Release`.
- [ ] Use **Build > Rebuild Solution**.
- [ ] Test the generated executable on a clean Windows folder.
- [ ] Confirm `Monitor Control` defaults to OFF.
- [ ] Confirm monitor controls are disabled while OFF.
- [ ] Confirm profile backup/restore works.
- [ ] Confirm Apply and Toggle hotkeys persist after restart.
- [ ] Test Game Auto + Toggle behavior.
- [ ] Test both Korean and English builds.

## Release assets

Recommended assets:

- `Tarkov-Gamma-Manager-v1.4.0-Korean.zip`
- `Tarkov-Gamma-Manager-v1.4.0-English.zip`
- `SHA256SUMS.txt`

Only upload binaries that were actually built and tested on Windows.
