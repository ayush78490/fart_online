-- Fart Online: accounts, friends and invites (Supabase / Postgres).
-- Run once in the Supabase SQL editor (or through the Management API). Safe to re-run: everything is
-- "create or replace" / "if not exists".
--
-- Model
--   auth.users          Supabase Auth (email + password). The sign-up request carries {"username": "..."} in its
--                       user metadata; a trigger creates the matching profile, so a taken username fails the sign-up.
--   public.profiles     one row per player: unique username (3-16 letters, digits, _), last_seen for "online" dots.
--   public.friendships  one row per pair: pending (requester -> addressee) or accepted.
--   public.invites      "come and raft with me in room ABC123", valid for 10 minutes.
-- Security
--   Row level security is on everywhere. Signed-in players may READ profiles and their own friendships / invites;
--   every WRITE goes through the security-definer functions below, which check auth.uid() themselves.

create extension if not exists citext;

-- ------------------------------------------------------------------ profiles
create table if not exists public.profiles (
    id         uuid primary key references auth.users (id) on delete cascade,
    username   citext not null unique,
    created_at timestamptz not null default now(),
    last_seen  timestamptz,
    constraint username_format check (username ~ '^[A-Za-z0-9_]{3,16}$')
);
alter table public.profiles enable row level security;
drop policy if exists "profiles are readable by signed-in players" on public.profiles;
create policy "profiles are readable by signed-in players" on public.profiles
    for select to authenticated using (true);

create or replace function public.handle_new_user() returns trigger
language plpgsql security definer set search_path = public as $$
declare
    u text := trim(coalesce(new.raw_user_meta_data ->> 'username', ''));
begin
    if u !~ '^[A-Za-z0-9_]{3,16}$' then
        raise exception 'invalid_username';
    end if;
    insert into public.profiles (id, username) values (new.id, u);
    return new;
end $$;

drop trigger if exists on_auth_user_created on auth.users;
create trigger on_auth_user_created after insert on auth.users
    for each row execute function public.handle_new_user();

-- sign-up form: is this username free? (callable before signing in)
create or replace function public.username_available(name text) returns boolean
language sql security definer set search_path = public stable as $$
    select name ~ '^[A-Za-z0-9_]{3,16}$'
       and not exists (select 1 from public.profiles p where p.username = name::citext);
$$;

-- the signed-in player's own profile
create or replace function public.my_profile() returns table (id uuid, username text)
language sql security definer set search_path = public stable as $$
    select p.id, p.username::text from public.profiles p where p.id = auth.uid();
$$;

-- "I'm online": the game calls this every 30 s while signed in
create or replace function public.heartbeat() returns void
language sql security definer set search_path = public as $$
    update public.profiles set last_seen = now() where id = auth.uid();
$$;

-- ------------------------------------------------------------------ friendships
create table if not exists public.friendships (
    id         bigint generated always as identity primary key,
    requester  uuid not null references public.profiles (id) on delete cascade,
    addressee  uuid not null references public.profiles (id) on delete cascade,
    status     text not null default 'pending',
    created_at timestamptz not null default now(),
    constraint friendship_status check (status in ('pending', 'accepted')),
    constraint friendship_not_self check (requester <> addressee)
);
create unique index if not exists friendships_pair
    on public.friendships (least(requester, addressee), greatest(requester, addressee));
alter table public.friendships enable row level security;
drop policy if exists "players see their own friendships" on public.friendships;
create policy "players see their own friendships" on public.friendships
    for select to authenticated using (auth.uid() in (requester, addressee));

-- Add a friend by username. Returns: sent | accepted (they had already asked you) | already_friends |
-- already_sent | not_found | self | not_signed_in
create or replace function public.send_friend_request(target_username text) returns text
language plpgsql security definer set search_path = public as $$
declare
    me     uuid := auth.uid();
    target uuid;
    f      public.friendships;
begin
    if me is null then return 'not_signed_in'; end if;
    select p.id into target from public.profiles p where p.username = trim(target_username)::citext;
    if target is null then return 'not_found'; end if;
    if target = me then return 'self'; end if;
    select * into f from public.friendships
     where least(requester, addressee) = least(me, target) and greatest(requester, addressee) = greatest(me, target);
    if found then
        if f.status = 'accepted' then return 'already_friends'; end if;
        if f.requester = me then return 'already_sent'; end if;
        update public.friendships set status = 'accepted' where id = f.id;     -- they asked first: accept
        return 'accepted';
    end if;
    insert into public.friendships (requester, addressee) values (me, target);
    return 'sent';
end $$;

-- Accept or decline a request sent to me (declining deletes it, so they may ask again later)
create or replace function public.respond_friend_request(request_id bigint, accept boolean) returns text
language plpgsql security definer set search_path = public as $$
declare me uuid := auth.uid();
begin
    if accept then
        update public.friendships set status = 'accepted'
         where id = request_id and addressee = me and status = 'pending';
    else
        delete from public.friendships where id = request_id and addressee = me and status = 'pending';
    end if;
    return case when found then 'ok' else 'not_found' end;
end $$;

-- Unfriend, or cancel a request I sent
create or replace function public.remove_friend(friend_id uuid) returns text
language plpgsql security definer set search_path = public as $$
declare me uuid := auth.uid();
begin
    delete from public.friendships
     where least(requester, addressee) = least(me, friend_id) and greatest(requester, addressee) = greatest(me, friend_id);
    return case when found then 'ok' else 'not_found' end;
end $$;

-- Friends list: accepted friends plus pending requests both ways. "online" = seen in the last 75 s.
create or replace function public.list_friends()
returns table (friend_id uuid, username text, status text, incoming boolean, request_id bigint, online boolean)
language sql security definer set search_path = public stable as $$
    select p.id, p.username::text, f.status, (f.addressee = auth.uid()) as incoming, f.id,
           coalesce(p.last_seen > now() - interval '75 seconds', false) as online
      from public.friendships f
      join public.profiles p on p.id = case when f.requester = auth.uid() then f.addressee else f.requester end
     where auth.uid() in (f.requester, f.addressee)
     order by (f.status = 'pending' and f.addressee = auth.uid()) desc,      -- requests to answer first
              coalesce(p.last_seen > now() - interval '75 seconds', false) desc,
              p.username;
$$;

-- ------------------------------------------------------------------ invites
create table if not exists public.invites (
    id         bigint generated always as identity primary key,
    from_user  uuid not null references public.profiles (id) on delete cascade,
    to_user    uuid not null references public.profiles (id) on delete cascade,
    room_code  text not null,
    status     text not null default 'pending',
    created_at timestamptz not null default now(),
    expires_at timestamptz not null default now() + interval '10 minutes',
    constraint invite_status check (status in ('pending', 'accepted', 'declined')),
    constraint invite_room_code check (room_code ~ '^[A-Z0-9]{4,8}$')
);
create index if not exists invites_to_user on public.invites (to_user, status, expires_at);
alter table public.invites enable row level security;
drop policy if exists "players see invites they sent or got" on public.invites;
create policy "players see invites they sent or got" on public.invites
    for select to authenticated using (auth.uid() in (from_user, to_user));

-- Invite an accepted friend to my room. Returns: sent | not_friends | bad_room | not_signed_in
create or replace function public.send_invite(friend_id uuid, room text) returns text
language plpgsql security definer set search_path = public as $$
declare me uuid := auth.uid();
begin
    if me is null then return 'not_signed_in'; end if;
    if room !~ '^[A-Z0-9]{4,8}$' then return 'bad_room'; end if;
    if not exists (select 1 from public.friendships
                    where status = 'accepted'
                      and least(requester, addressee) = least(me, friend_id)
                      and greatest(requester, addressee) = greatest(me, friend_id)) then
        return 'not_friends';
    end if;
    -- one live invite per friend: replace an older one
    delete from public.invites where from_user = me and to_user = friend_id and status = 'pending';
    insert into public.invites (from_user, to_user, room_code) values (me, friend_id, room);
    -- keep the table small
    delete from public.invites where expires_at < now() - interval '1 day';
    return 'sent';
end $$;

-- Invites waiting for me (newest first)
create or replace function public.my_invites()
returns table (invite_id bigint, from_username text, room_code text, created_at timestamptz)
language sql security definer set search_path = public stable as $$
    select i.id, p.username::text, i.room_code, i.created_at
      from public.invites i join public.profiles p on p.id = i.from_user
     where i.to_user = auth.uid() and i.status = 'pending' and i.expires_at > now()
     order by i.created_at desc
     limit 5;
$$;

create or replace function public.respond_invite(invite_id bigint, accept boolean) returns text
language plpgsql security definer set search_path = public as $$
begin
    update public.invites set status = case when accept then 'accepted' else 'declined' end
     where id = invite_id and to_user = auth.uid() and status = 'pending';
    return case when found then 'ok' else 'not_found' end;
end $$;

-- ------------------------------------------------------------------ grants
revoke all on function public.handle_new_user() from public, anon, authenticated;
grant execute on function public.username_available(text) to anon, authenticated;
grant execute on function public.my_profile(), public.heartbeat(), public.send_friend_request(text),
                          public.respond_friend_request(bigint, boolean), public.remove_friend(uuid),
                          public.list_friends(), public.send_invite(uuid, text), public.my_invites(),
                          public.respond_invite(bigint, boolean)
    to authenticated;
revoke execute on function public.my_profile(), public.heartbeat(), public.send_friend_request(text),
                           public.respond_friend_request(bigint, boolean), public.remove_friend(uuid),
                           public.list_friends(), public.send_invite(uuid, text), public.my_invites(),
                           public.respond_invite(bigint, boolean)
    from anon, public;
