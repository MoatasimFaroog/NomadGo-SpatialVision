# GitHub Secrets Setup (Required for CI Android Builds)

Configure these in **GitHub → Settings → Secrets and variables → Actions**.

## Required Unity secrets

| Secret | Purpose |
|---|---|
| `UNITY_EMAIL` | Unity account email used for CI activation. |
| `UNITY_PASSWORD` | Unity account password used for CI activation. |
| `UNITY_LICENSE` | Full contents of a valid Unity `.ulf` license file. |

## Optional Android signing secrets (release only)

> If these are missing, workflow still builds an unsigned debug APK.

| Secret | Purpose |
|---|---|
| `ANDROID_KEYSTORE_BASE64` | Base64 encoded release keystore file. |
| `ANDROID_KEYSTORE_PASS` | Keystore password. |
| `ANDROID_KEYALIAS_NAME` | Key alias name. |
| `ANDROID_KEYALIAS_PASS` | Key alias password. |

## Security policy

- Never commit keystore files or passwords to the repository.
- Never hardcode signing credentials in workflow files or docs.
- Rotate secrets immediately if exposed.
