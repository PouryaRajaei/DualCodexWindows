Dual Codex
==========

Run Codex and Codex Beta side by side with separate profiles and independent accounts.

Default profile folders
-----------------------
Codex:
%USERPROFILE%\.codex

Codex Beta:
%USERPROFILE%\.codex-beta

Use “Choose profile folder” to select a different folder for either version. Your
choices are saved locally and reused the next time the launcher starts.

How it works
------------
The launcher detects both Microsoft Store packages for the current Windows user.
If a version is missing, its button opens the official Microsoft Store page.

Before launching a version, the launcher closes only that version's existing
process so the correct CODEX_HOME value can be applied. The other version remains
open. Local DevTools ports 9223 (stable) and 9224 (beta) are used to apply the
built-in RTL helper.

Microsoft Store
---------------
Codex: https://apps.microsoft.com/detail/9plm9xgg6vks
Codex Beta: https://apps.microsoft.com/detail/9n8cj4w95tbz
