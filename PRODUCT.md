# Product

## Register

product

## Users

NoCTF serves two equally important user groups.

Competition participants use it during live CTF, AWD, AWDP, and KoH events. They need to find active competitions, enter the right workspace, read challenge state, submit flags or patches, track their team score, and react quickly to live attack or control changes.

Organizers and administrators use it before and during events to create competitions, configure challenges and containers, manage teams and collaborators, monitor health, inspect logs, audit sensitive actions, and keep the competition fair under pressure.

The product has a mixed surface model: the default design context is the product workbench, but public competition-facing screens can lean into brand and event energy when a task calls for it.

## Product Purpose

NoCTF is a modern competition platform for running Jeopardy CTF, AWD, AWDP, and KoH events with real-time scoring, plugin-driven game modes, and container-backed challenge orchestration.

Success means participants can compete without friction, organizers can operate the event with confidence, and the system makes fairness, status, and auditability visible. The interface should make complex live competition mechanics feel controlled rather than chaotic.

## Brand Personality

NoCTF should feel calm, precise, trustworthy, sharp, energetic, and technical.

The product should carry the pressure of live attack-defense competition without becoming theatrical. It should look like a serious control surface for a real event: crisp states, confident hierarchy, dense but legible data, and enough kinetic energy to make live rounds feel alive.

## Anti-references

Avoid generic SaaS admin design: white cards everywhere, soft marketing polish, flat dashboards, and no sense of competition.

Avoid overdone cyberpunk: neon black-purple surfaces, decorative glow, heavy visual effects, or anything that makes status and scores harder to read.

Avoid the default CTFd feel: traditional list-heavy competition pages, weak hierarchy, and a lack of modern control-room presence.

Avoid design choices that hide operational truth. Health, logs, scoring, audit trails, round state, and container status should be straightforward to inspect.

## Design Principles

1. Make fairness visible.
   Scores, submissions, audit logs, roles, and sensitive actions should feel traceable and trustworthy.

2. Treat live competition as a control room.
   Real-time state should be legible at a glance, with clear severity, connection state, and round context.

3. Separate participant momentum from organizer precision.
   Participant screens can be more energetic and event-focused. Admin screens should stay denser, calmer, and more operational.

4. Show the system's moving parts.
   Containers, plugins, checkers, patches, and health checks are part of the product promise. Surface them clearly instead of burying them.

5. Keep intensity useful.
   Visual energy should help users understand urgency, not decorate the interface or reduce readability.

## Accessibility & Inclusion

Public competition and spectator-facing surfaces should support large-screen readability, strong contrast, and fast comprehension from a distance.

Interactive product surfaces should preserve keyboard access, visible focus states, readable status labels, and color-independent signals for mode, severity, and connection state.

Motion should be purposeful and reducible. Live updates should avoid disorienting layout shifts, especially in scoreboards, logs, and round dashboards.
