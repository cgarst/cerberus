# Cerberus Expo Style Guide (Steam Library Theme)

This document defines the visual language, design tokens, and platform-specific UI patterns used across `CerberusExpo`, modeled after the desktop Steam Library client.

## 1. Color Tokens (`src/theme.ts`)

All colors live in `Colors` in [src/theme.ts](src/theme.ts).

### Base Surfaces & Chrome
| Token | Value | Usage |
| --- | --- | --- |
| `bg` | `#171d25` | Primary screen canvas |
| `bgDeep` | `#13171d` | Inset containers (sparkline wells, search inputs) |
| `chrome` | `#171a21` | Top navigation header & segmented sub-nav bar |
| `sidebar` | `#1c2128` | Compact list rows (shortcuts, open windows) |
| `surface` | `#212730` | Standard cards and panels |
| `surfaceElevated` | `#272e39` | Secondary buttons, active sub-nav tabs, header icon buttons |
| `surfaceGlass` | `rgba(31, 38, 48, 0.72)` | Web translucent glass fallback |
| `surfaceHover` | `#323a45` | Inactive switch tracks & hover/active highlights |
| `border` | `rgba(255, 255, 255, 0.08)` | Hairline card and row borders |
| `borderStrong` | `rgba(255, 255, 255, 0.14)` | Glass surface and secondary button borders |

### Typography Colors
| Token | Value | Usage |
| --- | --- | --- |
| `textBright` | `#ffffff` | Hero titles, primary CTA labels, active tab labels, metric values |
| `text` | `#dcdedf` | Body text, row titles, secondary button labels |
| `textSecondary` | `#8b929a` | Uppercase section headers, metadata labels, subtitles |
| `textMuted` | `#626972` | Input placeholders, offline status dots, target captions |

### Steam Signature & Semantic Accents
| Token | Value | Usage |
| --- | --- | --- |
| `steamBlue` | `#1a9fff` | Active tab underlines, featured card top bar, framerate sparkline, gauges, badges |
| `steamBlueBright` / `steamBlueDeep` | `#22a5ff` / `#1377c9` | Top-to-bottom `LinearGradient` for active-state Steam `EXIT <ROOM>` / `EXIT STREAM` stop buttons |
| `steamBlueBg` / `steamBlueBorder` | `rgba(26, 159, 255, 0.14)` / `0.42` | Living Room Play Bar tint/border, count badges, `Done` pill |
| `steamGreenBright` / `steamGreenDark` | `#6ec038` / `#437a1e` | Top-to-bottom `LinearGradient` for primary Steam `PLAY` / `SWITCH` / `WAKE` buttons |
| `purple` | `#a78bfa` | GameStream active state |
| `orange` | `#e59326` | Wake-in-progress & Sleep actions, 75–89% gauge warning |
| `red` | `#d94b4b` | Shutdown, Close Window, error banners, >=90% gauge critical |

---

## 2. Geometry & Radii (`Radii` in `src/theme.ts`)

Steam uses crisp, tight corners rather than large pill radii:
- `Radii.xs` (`3`): List rows, search inputs, tags, badges, mini action buttons
- `Radii.sm` (`4`): Primary green Play buttons, secondary buttons, sub-nav bar, sparkline wells, header icon buttons
- `Radii.md` (`6`): Content cards (`SteamSurface`), hero banner, modal cards
- `Radii.lg` (`8`): Larger elevated containers when needed
- `Radii.pill` (`999`): Circular status dots

---

## 3. Typography & Layout Conventions

- **Section Headers**: `11px`, `fontWeight: '700'`, uppercase, `letterSpacing: 0.9`, `Colors.textSecondary`.
- **Metadata Columns** (Steam Play Bar): `10px` uppercase tracked labels (`DISPLAY`, `CPU`, `GPU`, `FRAMERATE`) paired with `13px` `fontWeight: '600'` values and optional `4px`-tall `#1a9fff` progress bars.
- **Hero Banner** (`app/index.tsx`): Full-width `LinearGradient` header whose top-left tint reflects PC state (green for Office/Ready, Steam Blue for Living Room, purple for Streaming, amber for Waking, muted slate for Offline), with top-right status tags (`INSTALLED`, `OFFLINE`, `DEMO`).
- **Featured Cards**: Pass `featured` to `SteamSurface` to render Steam's `3px` `#1a9fff` top accent bar (used on `FEATURED · GAME STATS` and the web offline notice).
- **Sub-Nav Tabs**: Dark `#171a21` bar with `#272e39` active tab background and a `2px` `#1a9fff` bottom underline.

---

## 4. Platform-Specific Behavior

### Surface Hierarchy (`SteamSurface` in `src/components/SteamSurface.tsx`)
Use `<SteamSurface glass>` for elevated/overlay surfaces (Play Bar, mobile quick-nav tiles, Waking card, confirmation modal) and `<SteamSurface>` for standard content panels:
- **iOS 26+ (Liquid Glass)**: When `isLiquidGlassAvailable() && isGlassEffectAPIAvailable()` and `AccessibilityInfo.isReduceTransparencyEnabled()` is `false`, renders `GlassView` (`expo-glass-effect`) with `glassEffectStyle="regular"`, `colorScheme="dark"`, and optional `isInteractive` for pressable tiles.
- **Android & Older iOS**: Renders `BlurView` (`expo-blur`, `intensity={28}`, `tint="dark"`, `experimentalBlurMethod="dimezisBlurView"` on Android) with a dark Steam tint overlay.
- **Web**: Renders a translucent dark slate surface (`Colors.surfaceGlass`).

### Navigation Chrome (`app/_layout.tsx`)
- **iOS**: Translucent dark header (`rgba(23, 26, 33, 0.85)`) with `headerBlurEffect: 'systemChromeMaterialDark'`.
- **Android / Web**: Solid Steam chrome `#171a21` with `headerShadowVisible: false`.

### Touch & Haptics (`SteamPressable` & `src/utils/haptics.ts`)
- **Android**: `SteamPressable` enables native Material ripple (`android_ripple={{ color: Colors.ripple }}`).
- **iOS / Web**: `SteamPressable` applies `opacity: 0.78` on press.
- **Haptics**: Call `triggerHaptic('light' | 'medium' | 'success' | 'warning')` on interactive controls; automatically no-ops on web.
