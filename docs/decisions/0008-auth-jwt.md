# ADR 0008: Self-issued JWT with roles for authentication

**Status:** Accepted (implemented in a later milestone)

## Context
The brief needs two roles: `admin`, who can start and stop crawls, and `viewer`, who can only read.
There are only a handful of users.

## Decision
The API issues its own JWTs (signed with `Jwt__SigningKey`) carrying a role claim. Users are seeded
at startup from environment variables (`Seed__AdminPassword`, `Seed__ViewerPassword`). Endpoints
use role-based authorization; `/health` stays anonymous.

## Rejected alternative
**Keycloak** (or another identity provider). It's realistic for production, but it's one more
container plus realm configuration, for a feature the brief covers with two roles.

## Consequences
- No extra container. The whole flow is visible in a few files.
- We own key handling: the signing key comes from the environment, never from the repo.
- No SSO, refresh-token rotation or user self-service. None of that is required.
