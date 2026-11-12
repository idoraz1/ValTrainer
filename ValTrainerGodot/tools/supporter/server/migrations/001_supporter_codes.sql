-- Every supporter payment, once (the webhook skips retries), with the code made for it.
create table public.supporter_codes (
  id          bigint generated always as identity primary key,
  created_at  timestamptz not null default now(),
  platform    text not null check (platform in ('kofi', 'bmc', 'manual')),
  tx_id       text not null,
  name        text not null,
  email       text,
  amount      text,
  currency    text,
  kind        text,
  test        boolean not null default false,
  code        text not null,
  emailed     boolean not null default false,
  error       text,
  unique (platform, tx_id)
);
comment on table public.supporter_codes is 'ValTrainer supporter codes made by the supporter-webhook edge function. Server-only: RLS on, no policies.';
-- RLS on and no policies: the anon / authenticated API roles see nothing; only the service role (the edge function) can.
alter table public.supporter_codes enable row level security;
revoke all on public.supporter_codes from anon, authenticated;
