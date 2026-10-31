# ModProperties regression checks

Run from the repository root:

~~~powershell
dotnet run --project tests/ModProperties.Checks/ModProperties.Checks.csproj -p:BuildAndroid=false
~~~

The headless runner checks selected-package dependency traversal, shared requirements, legacy duplicate filtering, developer activation, normal package removal, clean reinstall commit/rollback, cancellation, initial version selection, stale responses, internal confirmations, compression restrictions, and desktop/phone layouts. It also covers version deletion, cancellation of its confirmation, protection of development and retail installations, and closing/reopening Properties during install or verify. Running-session checks cover live progress after reopening and automatic cleanup after completion, cancellation, and failure while the window is closed.

It uses an in-memory catalog and a disposable library under artifacts/mod-versions-checks/<run-id>. No Nebula requests, downloads, or installed user mods are involved. Each run also saves desktop and phone preview images there.

Queue checks cover multiple Properties requests, shared-package unions, stale new-install selections, independent progress and cancellation, developer activation, FSO reuse after waiting, cleanup ordering after failure, and old-version reservations for pending dependencies. Empty local packages keep these checks independent of network access.
