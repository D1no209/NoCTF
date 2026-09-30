# Shared motion library

`presets.ts` returns presentation attributes, and `motion.css` owns timing, keyframes and reduced-motion behavior. The library has no dependency on features, API clients or application state.

- `list-enter`: subtle horizontal entrance with bounded item staggering (maximum 150ms).
- `detail-enter`: brief vertical entrance for a newly selected detail surface.
- `noctf-motion-interactive`: shared transitions for hover/focus colors and borders.
- `animateLocaleLayout`: measures intrinsic shared-control and top-navigation slot widths before and after a locale update, then expands or contracts them over 500ms with the Web Animations API. Navigation capsules follow the slot widths without scaling their text or icons. Interrupted animations are measured at their current width and cancelled before measuring the new intrinsic target. Reduced motion applies the locale immediately.
- `topNavMotionDirective`: applies the final navigation width in one layout pass, then uses FLIP transforms with an inverse content transform so labels remain unscaled. Pointer, keyboard focus and active-route expansion retain the existing 760ms timing.
- `film-up` content swap: `useContentSwap` sequences a 160ms upward exit and a 220ms entrance from below. `film-left` and `film-right` use the same sequencing for a horizontal poster reel and can react to selection direction. `MotionSwap` keeps the outer card separate and reserves outgoing height while the content changes. Completion/cancellation releases the reservation.
- `useWaveMotion`: pointer/focus position produces a smooth distance-based displacement across neighboring items. The peak moves outward 30px; the selected item keeps a 14px resting offset. The library never changes selection. Geometry is cached, updates are coalesced into a requested animation frame, unchanged styles are skipped, and no idle animation loop runs. Observers and frames are disposed with the consumer.
- `useScrollDockMotion`: ScrollDock keeps the poster, metadata and body in one native scroll surface. Measured title, countdown and action groups scale and move into a compact top row; secondary metadata fades out. The row reserves its measured height, while body clipping prevents overlap without a background mask. Reverse scrolling eases back to the expanded layout. Scroll coordinates are never rewritten, reduced motion switches layouts immediately, and frames/listeners are disposed on unmount.
- `noctf-disclosure-content` / `noctf-disclosure-chevron`: shared group opening, closing and arrow rotation for collapsible lists. One wave surface spans all expanded groups. Reduced motion disables the disclosure animations and transitions.
- `useTypewriterMotion`: sequential Unicode code-point reveal for the current pseudo-terminal output. It restarts when a command replaces the output, pauses while the document is hidden, reveals immediately for reduced motion and disposes its timer after completion or unmount.
- `status-mark`: 320ms scale-and-rotate reveal for newly mounted challenge completion marks. The settled rotation comes from the status token; reduced motion disables the reveal.

UI primitives call `motionAttributes`. `MotionSurface` bridges a preset to content composed by rendering views. Selection, keyboard navigation and network state do not belong in this library. Do not attach global keyboard handlers, hijack Tab, or create one animation loop per item.

Both animation presets are disabled by `prefers-reduced-motion: reduce`. Library styling is imported once by the shared stylesheet.

The film transition also respects reduced motion. It uses Vue's out-in transition sequencing, so rapid selection resolves to the latest keyed content. Feature instances must additionally carry their own stable identity key.
