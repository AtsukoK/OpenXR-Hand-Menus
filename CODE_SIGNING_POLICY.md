# Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).

- Only release builds made by this repository's GitHub Actions workflow from the public source are signed. Nothing built on a personal machine is signed.
- Signed file: `OpenXRHandMenusEditor.exe`. The layer DLL is loaded by the OpenXR loader and is not signed.
- Committers and reviewers: the repository owner. Approver for each signing request: the repository owner.
- Privacy: the only network connection is an optional update check. When the editor starts, it reads the public "latest release" record of this repository from the GitHub API, and it can be switched off in Settings. No personal data is sent, and nothing is downloaded or installed automatically. The OpenXR layer itself makes no network connections. Profiles and logs stay in `%LOCALAPPDATA%\OpenXRHandMenus\`.
