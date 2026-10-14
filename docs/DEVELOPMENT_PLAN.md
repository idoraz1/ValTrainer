# Development plan

This document describes proposed follow-up work. It does not add any of those systems, assets or sound banks to this pull request.

## Weapons

1. Audit the current weapon backlog before implementation.
2. Add gameplay behavior and presentation in reviewable, separate changes.
3. Include a model only when it is self-made or has a license compatible with this repository's contribution policy.

## Materials and movement Foley

1. Define the supported surface set and the expected gameplay feedback for each surface.
2. Verify the license and provenance of every source sound before importing it.
3. Test individual sound banks for audibility, consistency and performance before proposing integration.

## Validation

Each follow-up change will include automated checks where suitable, a headless smoke test and manual in-game verification. Test polygons and experimental assets will remain outside upstream contributions until they are ready for review.

No Valve-derived or AI-restricted assets are planned for contribution.
