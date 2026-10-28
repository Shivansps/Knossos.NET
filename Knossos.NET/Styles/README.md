# Futuristic UI

Add `Classes="Futuristic"` to a view root to opt into Futuristic.axaml, which is included after the Fluent theme. Existing screens retain their appearance until they opt in. Shared resource names describe visual roles rather than individual screens.

## Visual language

- Deep blue-black backgrounds use FuturisticBackdrop, a subtle diagonal gradient.
- Panels use FuturisticSurface (#192229), a one-pixel FuturisticBorder (#33424E), and a six-pixel corner radius.
- Text uses FuturisticText (#F3F6FA), with FuturisticMuted (#A9B8C8) for secondary labels. Use the application Inter font: 14px body, 16px supporting text, 30px bold page titles.
- FuturisticAccent (#28ACFF) marks active controls. Selected navigation also uses a leading border and heavier text.
- Use a 4px spacing unit, 12–24px page padding, 12–16px between sections, and at least 44px height for interactive controls.
- Actions use green for Play, amber for Debug, copper for Fred2, blue for settings and primary actions, and red for destructive actions. Always include text labels.
- Reuse Futuristic*Icon vector resources, normally at 20px or 16px in compact navigation.

## Classes

| Element | Classes | Purpose |
| --- | --- | --- |
| Border | surface | Bordered inset panel |
| TextBlock | heading, muted | Page title, secondary text |
| Button | play, debug, editor, accent, danger | Action roles |
| Button | navigation, selected | Sidebar entry and active state |
| Button | mediaTab, selected | Underlined gallery tab |
| Button | thumbnail | Edge-to-edge media tile |

Override the named brush resources to theme the design. Keep component layout in its view; reusable styles belong here. Hover, pressed, and disabled states are defined centrally.

## Mod Properties

ModPropertiesView owns navigation and internal overlays. Child pages are UserControls; their views and viewmodels live in the corresponding ModProperties folders. Explicit data templates support Android trimming without reflection for these pages.

Pass MenuPageEntry.Information (default) or MenuPageEntry.Options / Settings to the viewmodel constructor to choose the initial page. The list constructor preserves the selected installed version and its library card. Launch Options reuses the existing settings behavior and saves on navigation, version changes, and closing. Versions, DLC, and Local Files are reserved, disabled entries until their pages are implemented; update/modify still use the existing installer.

Breakpoints use Avalonia logical pixels. Below 760px the sidebar narrows while retaining icons and labels. Below 940px of content width, description and media stack. Metadata and launch actions wrap; thumbnails adapt to one, two, or three columns. Each page scrolls independently of the sidebar.

Screenshots, reports, and deletion confirmation use an internal overlay. Escape dismisses it, arrow keys navigate screenshots, and focus returns on dismissal. AnimatedImage supports APNG banners and full-size captures. Remote images use the existing cache asynchronously; canceled version requests do not publish stale content. YouTube links are validated and normalized; an explicit preview button opens playback in the platform browser or YouTube app.
