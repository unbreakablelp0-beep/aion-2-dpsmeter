-- AionMeta – Bestenliste, Erweiterung 1 (schnellste Kills, Klassen-Rangliste, Spieler der Woche).
-- Einmal im Supabase-Dashboard unter „SQL Editor“ einfügen und „Run“ klicken. Liest nur vorhandene Daten, sammelt nichts Neues.

-- Schnellste Kills: die 10 kürzesten Kämpfe je Boss.
create or replace view public.fastest_kills as
select boss, player_name, class, duration_s, dps, created_at
from (
  select boss, coalesce(player, 'Anonym') as player_name, class, duration_s, dps, created_at,
         row_number() over (partition by boss order by duration_s asc, dps desc) as place
  from public.fights
) ranked
where place <= 10;

-- Klassen-Rangliste über alle Bosse.
create or replace view public.class_ranking as
select class, count(*) as fights, round(avg(dps)) as avg_dps, max(dps) as best_dps
from public.fights
group by class;

-- Spieler der Woche: beste DPS der letzten 7 Tage – nur Spieler, die ihren Namen freigegeben haben.
create or replace view public.player_of_week as
select player as player_name, class, boss, dps, duration_s, created_at
from public.fights
where player is not null and created_at > now() - interval '7 days'
order by dps desc
limit 5;

grant select on public.fastest_kills, public.class_ranking, public.player_of_week to anon;
