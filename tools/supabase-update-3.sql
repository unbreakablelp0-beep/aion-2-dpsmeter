-- AionMeta – Bestenliste, Erweiterung 3: Nicht-Bosse ausblenden (z. B. „Strahlendes Totem (Mau)“).
-- Einmal im Supabase-Dashboard unter „SQL Editor“ einfügen und „Run“ klicken.
-- Es wird NICHTS gelöscht: Einträge mit Namen aus der Liste „hidden_bosses“ werden in allen Ansichten nur ausgeblendet.
-- Weitere Namen kannst du später im „Table Editor“ in der Tabelle hidden_bosses hinzufügen (oder wieder entfernen).

create table if not exists public.hidden_bosses (name text primary key);
alter table public.hidden_bosses enable row level security;
revoke all on public.hidden_bosses from anon, authenticated;
insert into public.hidden_bosses (name) values ('Strahlendes Totem (Mau)') on conflict do nothing;

-- Gültige Kämpfe = alle außer ausgeblendeten Namen und Objekten/Übungszielen.
create or replace view public.fights_visible as
select * from public.fights f
where not exists (select 1 from public.hidden_bosses h where h.name = f.boss)
  and f.boss !~* '(totem|trainingspuppe|sandsack|puppe|wachturm|reaktor|dimensionskern|kristall|siegelstein|säule|statue|generator)';
revoke all on public.fights_visible from anon, authenticated;

create or replace view public.leaderboard as
select boss, player_name, class, dps, duration_s, created_at
from (select boss, coalesce(player, 'Anonym') as player_name, class, dps, duration_s, created_at,
             row_number() over (partition by boss order by dps desc) as place
      from public.fights_visible) ranked
where place <= 20;

create or replace view public.class_stats as
select boss, class, count(*) as fights, round(avg(dps)) as avg_dps, max(dps) as best_dps
from public.fights_visible group by boss, class;

create or replace view public.totals as
select count(*) as fights, count(distinct boss) as bosses, max(created_at) as last_fight from public.fights_visible;

create or replace view public.fastest_kills as
select boss, player_name, class, duration_s, dps, created_at
from (select boss, coalesce(player, 'Anonym') as player_name, class, duration_s, dps, created_at,
             row_number() over (partition by boss order by duration_s asc, dps desc) as place
      from public.fights_visible) ranked
where place <= 10;

create or replace view public.class_ranking as
select class, count(*) as fights, round(avg(dps)) as avg_dps, max(dps) as best_dps
from public.fights_visible group by class;

create or replace view public.player_of_week as
select player as player_name, class, boss, dps, duration_s, created_at
from public.fights_visible
where player is not null and created_at > now() - interval '7 days'
order by dps desc limit 5;

create or replace view public.leaderboard_table as
select boss, player_name, class, gs, ks, dps, damage, duration_s, created_at
from (select boss, coalesce(player, 'Anonym') as player_name, class, gs, ks, dps, damage, duration_s, created_at,
             row_number() over (partition by boss order by dps desc) as place
      from public.fights_visible) ranked
where place <= 100;

create or replace view public.class_week_stats as
with this_week as (
  select boss, class, count(*) as values_count,
         round(percentile_cont(0.9)  within group (order by dps)) as top10,
         round(percentile_cont(0.75) within group (order by dps)) as top25,
         round(percentile_cont(0.5)  within group (order by dps)) as median,
         max(dps) as best
  from public.fights_visible where created_at > now() - interval '7 days' group by boss, class
), last_week as (
  select boss, class, round(percentile_cont(0.5) within group (order by dps)) as median
  from public.fights_visible
  where created_at <= now() - interval '7 days' and created_at > now() - interval '14 days' group by boss, class
)
select t.boss, t.class, t.values_count, t.top10, t.top25, t.median, t.best, l.median as median_last_week
from this_week t left join last_week l on l.boss = t.boss and l.class = t.class;

grant select on public.leaderboard, public.class_stats, public.totals, public.fastest_kills, public.class_ranking,
  public.player_of_week, public.leaderboard_table, public.class_week_stats to anon;
