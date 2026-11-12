-- GET MY CODE (in-game claim by email): when a code was claimed, and a log of claim attempts for the rate limit.
alter table public.supporter_codes add column if not exists claimed_at timestamptz;
create index if not exists supporter_codes_email_idx on public.supporter_codes (email) where not test;

create table if not exists public.supporter_claims (
  id     bigint generated always as identity primary key,
  at     timestamptz not null default now(),
  ip     text not null,
  found  boolean not null default false
);
create index if not exists supporter_claims_ip_at_idx on public.supporter_claims (ip, at);
comment on table public.supporter_claims is 'Claim attempts (rate limit: 5 per address per hour). Server-only: RLS on, no policies.';
alter table public.supporter_claims enable row level security;
revoke all on public.supporter_claims from anon, authenticated;
