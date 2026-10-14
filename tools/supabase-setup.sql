-- AionMeta – Bestenliste (Supabase). Einmal im Supabase-Dashboard unter „SQL Editor“ einfügen und „Run“ klicken.
-- Gespeichert werden nur Kampf-Zusammenfassungen von Spielern, die das Teilen in AionMeta eingeschaltet haben:
-- Boss, Dauer, Schaden, DPS, Klasse, AionMeta-Version und – nur wenn der Spieler das will – sein eigener Name.
-- Namen anderer Spieler werden nie gesendet. Anonyme Nutzer dürfen nur EINFÜGEN, nichts lesen, ändern oder löschen;
-- gelesen werden nur die zusammengefassten Ansichten unten.

create table if not exists public.fights (
  id          bigint generated always as identity primary key,
  created_at  timestamptz not null default now(),
  boss        text    not null check (char_length(boss) between 1 and 80),
  duration_s  integer not null check (duration_s between 10 and 7200),
  damage      bigint  not null check (damage between 1 and 100000000000),
  dps         integer not null check (dps between 1 and 50000000),
  class       text    not null check (char_length(class) between 1 and 30),
  version     text    not null check (version ~ '^[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,4}$'),
  player      text    check (player is null or char_length(player) between 2 and 24)
);

alter table public.fights enable row level security;

-- Nur Einfügen für anonyme Nutzer (die App). Kein Lesen, Ändern oder Löschen der Rohdaten.
drop policy if exists "AionMeta darf Kaempfe einfuegen" on public.fights;
create policy "AionMeta darf Kaempfe einfuegen" on public.fights for insert to anon with check (true);
revoke select, update, delete on public.fights from anon, authenticated;
grant insert on public.fights to anon;

-- Bestenliste: die 20 besten DPS je Boss (Name nur, wenn der Spieler ihn freigegeben hat, sonst „Anonym“).
create or replace view public.leaderboard as
select boss, player_name, class, dps, duration_s, created_at
from (
  select boss,
         coalesce(player, 'Anonym') as player_name,
         class, dps, duration_s, created_at,
         row_number() over (partition by boss order by dps desc) as place
  from public.fights
) ranked
where place <= 20;

-- Statistik je Boss und Klasse: Anzahl Kämpfe, Durchschnitts- und Bestwert.
create or replace view public.class_stats as
select boss, class, count(*) as fights, round(avg(dps)) as avg_dps, max(dps) as best_dps
from public.fights
group by boss, class;

-- Gesamtzahlen für die Webseite.
create or replace view public.totals as
select count(*) as fights, count(distinct boss) as bosses, max(created_at) as last_fight
from public.fights;

grant select on public.leaderboard, public.class_stats, public.totals to anon;
