# Copilot Instructions

## Project Guidelines
- In the iRacing Spotter Generator WPF project, TakesWindow must preserve ALL recorded/trimmed takes per message (not just the single selected one) using a stable per-message temp folder (keyed by SpotterMessage.Id), since takes were previously lost as soon as the recording dialog was closed.