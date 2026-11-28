-- Discord Supporter role (/supporter in the ValTrainer server): which Discord account a payment was linked to, so one
-- donation gives the role to one account only.
alter table public.supporter_codes add column if not exists discord_id text;
alter table public.supporter_codes add column if not exists discord_linked_at timestamptz;
