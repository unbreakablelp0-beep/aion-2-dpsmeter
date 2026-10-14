-- AionMeta – Bestenliste, Erweiterung 2 (eigener GS und eigene KS, Bestenliste als Tabelle, Klassen-Statistik).
-- Einmal im Supabase-Dashboard unter „SQL Editor“ einfügen und „Run“ klicken. Rückwärtskompatibel: ältere AionMeta-
-- Versionen ohne GS/KS können weiter eintragen. Die Regeln (nur Einfügen, keine Rohdaten lesen) bleiben unverändert.

alter table public.fights add column if not exists gs integer check (gs is null or gs between 1 and 10000);
alter table public.fights add column if not exists ks bigint  check (ks is null or ks between 1000 and 10000000);

-- Bestenliste als Tabelle: die 100 besten DPS je Boss, mit GS, KS, Schaden, Zeit und Datum.
create or replace view public.leaderboard_table as
select boss, player_name, class, gs, ks, dps, damage, duration_s, created_at
from (
  select boss, coalesce(player, 'Anonym') as player_name, class, gs, ks, dps, damage, duration_s, created_at,
         row_number() over (partition by boss order by dps desc) as place
  from public.fights
) ranked
where place <= 100;

-- Klassen-Statistik der letzten 7 Tage je Boss: Anzahl, Top 10 %, Top 25 %, Median, Höchstwert, Median der Vorwoche.
create or replace view public.class_week_stats as
with this_week as (
  select boss, class,
         count(*) as values_count,
         round(percentile_cont(0.9)  within group (order by dps)) as top10,
         round(percentile_cont(0.75) within group (order by dps)) as top25,
         round(percentile_cont(0.5)  within group (order by dps)) as median,
         max(dps) as best
  from public.fights
  where created_at > now() - interval '7 days'
  group by boss, class
), last_week as (
  select boss, class, round(percentile_cont(0.5) within group (order by dps)) as median
  from public.fights
  where created_at <= now() - interval '7 days' and created_at > now() - interval '14 days'
  group by boss, class
)
select t.boss, t.class, t.values_count, t.top10, t.top25, t.median, t.best, l.median as median_last_week
from this_week t left join last_week l on l.boss = t.boss and l.class = t.class;

grant select on public.leaderboard_table, public.class_week_stats to anon;
