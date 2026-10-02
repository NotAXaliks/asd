-- NEON ARENA — демо-БД компьютерного клуба
-- Заливать в базу proday:
--   docker exec -i postgres psql -U xaliks -d proday -v ON_ERROR_STOP=1 < database.sql

DROP FUNCTION IF EXISTS expire_sessions();
DROP FUNCTION IF EXISTS reset_demo();
DROP TABLE IF EXISTS topups, players, audit_log, payments, sessions, pcs, games CASCADE;

CREATE TABLE games (
  id    serial PRIMARY KEY,
  name  text NOT NULL,
  color text NOT NULL
);

CREATE TABLE pcs (
  id          serial PRIMARY KEY,
  name        text NOT NULL,
  zone        text NOT NULL,
  hourly_rate numeric(10,2) NOT NULL,
  status      text NOT NULL DEFAULT 'free' CHECK (status IN ('off','free','busy'))
);

CREATE TABLE sessions (
  id         serial PRIMARY KEY,
  pc_id      int NOT NULL REFERENCES pcs(id),
  game_id    int REFERENCES games(id),
  player     text NOT NULL,
  started_at timestamptz NOT NULL DEFAULT now(),
  ends_at    timestamptz NOT NULL,
  ended_at   timestamptz
);
CREATE INDEX ON sessions (pc_id) WHERE ended_at IS NULL;

CREATE TABLE payments (
  id         serial PRIMARY KEY,
  pc_id      int REFERENCES pcs(id),
  session_id int REFERENCES sessions(id),
  kind       text NOT NULL CHECK (kind IN ('session','extend','bar')),
  title      text NOT NULL,
  amount     numeric(10,2) NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ON payments (created_at);

CREATE TABLE audit_log (
  id         bigserial PRIMARY KEY,
  created_at timestamptz NOT NULL DEFAULT now(),
  pc_id      int REFERENCES pcs(id),
  type       text NOT NULL,
  message    text NOT NULL,
  source     text NOT NULL DEFAULT 'system'
);

-- Игроки с личным балансом (предоплата)
CREATE TABLE players (
  id       serial PRIMARY KEY,
  nickname text NOT NULL UNIQUE,
  balance  numeric(10,2) NOT NULL DEFAULT 0
);

-- Пополнения баланса: это деньги в кассе, но ещё НЕ выручка
CREATE TABLE topups (
  id         serial PRIMARY KEY,
  player_id  int NOT NULL REFERENCES players(id),
  amount     numeric(10,2) NOT NULL,      -- внесено
  bonus      numeric(10,2) NOT NULL DEFAULT 0,  -- подарено клубом
  created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ON topups (created_at);

INSERT INTO players (nickname) VALUES
  ('Shadow'),('Kira'),('xX_Pro_Xx'),('Nagibator'),('Frost'),('Luna'),
  ('Ghost'),('Tanker'),('Rin'),('Zeus'),('Hitman'),('Pudge_Main');

INSERT INTO games (name, color) VALUES
  ('Dota 2',    '#E74C3C'),
  ('CS2',       '#F39C12'),
  ('Valorant',  '#FF4F8B'),
  ('Fortnite',  '#8B5CF6'),
  ('Minecraft', '#22C55E'),
  ('GTA V',     '#3B82F6');

INSERT INTO pcs (name, zone, hourly_rate) VALUES
  ('PC-01', 'Standard', 150),
  ('PC-02', 'Standard', 150),
  ('PC-03', 'Standard', 150),
  ('PC-04', 'Standard', 150),
  ('PC-05', 'VIP',      250),
  ('PC-06', 'VIP',      250),
  ('PC-07', 'Standard', 150),
  ('PC-08', 'Standard', 150),
  ('PC-09', 'Standard', 150);

-- Истечение сессий: атомарно, можно звать с нескольких клиентов
CREATE FUNCTION expire_sessions() RETURNS int LANGUAGE plpgsql AS $$
DECLARE r record; n int := 0;
BEGIN
  FOR r IN UPDATE sessions s SET ended_at = s.ends_at
           WHERE s.ended_at IS NULL AND s.ends_at <= now()
           RETURNING s.pc_id, s.player
  LOOP
    UPDATE pcs SET status = 'free' WHERE id = r.pc_id AND status = 'busy';
    INSERT INTO audit_log (pc_id, type, message, source)
    SELECT r.pc_id, 'session_expired', p.name || ' · время ' || r.player || ' истекло', 'system'
    FROM pcs p WHERE p.id = r.pc_id;
    n := n + 1;
  END LOOP;
  RETURN n;
END $$;

-- Генерация «живой» истории за 7 дней относительно now()
CREATE FUNCTION reset_demo() RETURNS void LANGUAGE plpgsql AS $$
DECLARE
  nicks  text[] := ARRAY['Shadow','Kira','xX_Pro_Xx','Nagibator','Frost','Luna','Ghost','Tanker','Rin','Zeus','Hitman','Pudge_Main'];
  durs   int[]  := ARRAY[30,60,60,90,120,180];
  gw     int[]  := ARRAY[1,1,1,2,2,2,3,3,4,5,6];
  bar_n  text[] := ARRAY['Энергетик','Кола','Чипсы','Пицца','Кофе','Бургер'];
  bar_p  int[]  := ARRAY[150,100,120,390,130,290];
  d int; pc record; t timestamptz; w_start timestamptz; w_end timestamptz;
  dur int; g int; nick text; sid int; amt numeric; bi int; weekend bool; gname text;
BEGIN
  TRUNCATE audit_log, payments, sessions, topups RESTART IDENTITY;
  UPDATE players SET balance = 0;
  UPDATE pcs SET status = 'free';

  FOR d IN 0..6 LOOP
    IF d = 0 THEN
      w_start := now() - interval '11 hours' - interval '50 minutes';
      w_end   := now() - interval '13 minutes';
    ELSE
      w_start := now() - d * interval '1 day' - interval '11 hours 50 minutes';
      w_end   := now() - d * interval '1 day';
    END IF;
    weekend := extract(isodow FROM w_start) >= 6;

    FOR pc IN SELECT * FROM pcs ORDER BY id LOOP
      t := w_start + floor(random() * 40) * interval '1 minute';
      WHILE t < w_end LOOP
        dur  := durs[1 + floor(random() * array_length(durs, 1))::int];
        g    := gw[1 + floor(random() * array_length(gw, 1))::int];
        nick := nicks[1 + floor(random() * array_length(nicks, 1))::int];
        EXIT WHEN d = 0 AND t + dur * interval '1 minute' > now() - interval '12 minutes';

        INSERT INTO sessions (pc_id, game_id, player, started_at, ends_at, ended_at)
        VALUES (pc.id, g, nick, t, t + dur * interval '1 minute', t + dur * interval '1 minute')
        RETURNING id INTO sid;

        amt := round(pc.hourly_rate * dur / 60.0 / 10) * 10;
        INSERT INTO payments (pc_id, session_id, kind, title, amount, created_at)
        VALUES (pc.id, sid, 'session', 'Тариф ' || dur || ' мин', amt, t);

        IF random() < 0.45 THEN
          bi := 1 + floor(random() * 6)::int;
          INSERT INTO payments (pc_id, session_id, kind, title, amount, created_at)
          VALUES (pc.id, sid, 'bar', bar_n[bi], bar_p[bi], t + random() * dur * interval '1 minute');
          IF d = 0 THEN
            INSERT INTO audit_log (created_at, pc_id, type, message)
            VALUES (t + interval '7 minutes', pc.id, 'bar', pc.name || ' · ' || bar_n[bi] || ' · +' || bar_p[bi] || ' ₽');
          END IF;
        END IF;

        IF d = 0 THEN
          SELECT name INTO gname FROM games WHERE id = g;
          INSERT INTO audit_log (created_at, pc_id, type, message) VALUES
            (t, pc.id, 'session_start', pc.name || ' · ' || nick || ': сессия ' || dur || ' мин · +' || amt || ' ₽'),
            (t + interval '2 seconds', pc.id, 'game', pc.name || ' · запущена ' || gname),
            (t + dur * interval '1 minute', pc.id, 'session_end', pc.name || ' · сессия ' || nick || ' завершена (' || dur || ' мин)');
        END IF;

        t := t + (dur + CASE WHEN weekend THEN 5 + random() * 15 ELSE 5 + random() * 40 END) * interval '1 minute';
      END LOOP;
    END LOOP;
  END LOOP;

  -- Последние 12 минут — как в авто-режиме: короткие сессии по 10–20 секунд
  FOR pc IN SELECT * FROM pcs ORDER BY id LOOP
    t := now() - interval '12 minutes' + random() * interval '20 seconds';
    WHILE t < now() - interval '25 seconds' LOOP
      EXIT WHEN pc.id = 4 AND t > now() - interval '6 minutes 30 seconds';
      dur  := 10 + floor(random() * 11)::int;
      g    := gw[1 + floor(random() * array_length(gw, 1))::int];
      nick := nicks[1 + floor(random() * array_length(nicks, 1))::int];
      SELECT name INTO gname FROM games WHERE id = g;

      INSERT INTO sessions (pc_id, game_id, player, started_at, ends_at, ended_at)
      VALUES (pc.id, g, nick, t, t + dur * interval '1 second', t + dur * interval '1 second')
      RETURNING id INTO sid;

      amt := round(pc.hourly_rate * 0.25 / 10) * 10;
      INSERT INTO payments (pc_id, session_id, kind, title, amount, created_at)
      VALUES (pc.id, sid, 'session', 'Тариф 15 мин', amt, t);
      INSERT INTO audit_log (created_at, pc_id, type, message) VALUES
        (t, pc.id, 'session_start', pc.name || ' · ' || nick || ': 15 мин · +' || amt || ' ₽'),
        (t + interval '5 milliseconds', pc.id, 'game', pc.name || ' · запущена ' || gname),
        (t + dur * interval '1 second', pc.id, 'session_expired', pc.name || ' · время ' || nick || ' истекло');

      IF random() < 0.08 THEN
        bi := 1 + floor(random() * 6)::int;
        INSERT INTO payments (pc_id, session_id, kind, title, amount, created_at)
        VALUES (pc.id, sid, 'bar', bar_n[bi], bar_p[bi], t + interval '4 seconds');
        INSERT INTO audit_log (created_at, pc_id, type, message)
        VALUES (t + interval '4 seconds', pc.id, 'bar', pc.name || ' · ' || bar_n[bi] || ' · +' || bar_p[bi] || ' ₽');
      END IF;

      t := t + (dur + 2 + random() * 10) * interval '1 second';
    END LOOP;
  END LOOP;

  -- Живое состояние «прямо сейчас»: сессии заканчиваются через 6–17 секунд
  INSERT INTO sessions (pc_id, game_id, player, started_at, ends_at) VALUES
    (1, 1, 'Pudge_Main', now() - interval '12 seconds', now() + interval '6 seconds'),
    (2, 2, 'Kira',       now() - interval '5 seconds',  now() + interval '15 seconds'),
    (5, 3, 'Shadow',     now() - interval '8 seconds',  now() + interval '12 seconds'),
    (8, 5, 'Luna',       now() - interval '3 seconds',  now() + interval '17 seconds');
  UPDATE pcs SET status = 'busy' WHERE id IN (1, 2, 5, 8);
  UPDATE pcs SET status = 'off'  WHERE id = 4;

  INSERT INTO payments (pc_id, session_id, kind, title, amount, created_at)
  SELECT s.pc_id, s.id, 'session', 'Тариф 1 час', p.hourly_rate, s.started_at
  FROM sessions s JOIN pcs p ON p.id = s.pc_id WHERE s.ended_at IS NULL;

  INSERT INTO audit_log (created_at, pc_id, type, message)
  SELECT s.started_at, s.pc_id, 'session_start', p.name || ' · ' || s.player || ': сессия 1 час · +' || round(p.hourly_rate) || ' ₽'
  FROM sessions s JOIN pcs p ON p.id = s.pc_id WHERE s.ended_at IS NULL
  UNION ALL
  SELECT s.started_at + interval '2 seconds', s.pc_id, 'game', p.name || ' · запущена ' || gm.name
  FROM sessions s JOIN pcs p ON p.id = s.pc_id JOIN games gm ON gm.id = s.game_id WHERE s.ended_at IS NULL;

  INSERT INTO audit_log (created_at, pc_id, type, message)
  VALUES (now() - interval '6 minutes', 4, 'power_off', 'PC-04 · выключен');

  -- Пополнения за неделю + текущие балансы
  INSERT INTO topups (player_id, amount, bonus, created_at)
  SELECT 1 + floor(random() * 12)::int, a, CASE WHEN a >= 2000 THEN a * 0.15 WHEN a >= 1000 THEN a * 0.1 ELSE 0 END,
         now() - random() * interval '7 days'
  FROM (SELECT (ARRAY[300,500,500,1000,1000,2000])[1 + floor(random() * 6)::int] AS a FROM generate_series(1, 60)) x;

  INSERT INTO audit_log (created_at, type, message)
  SELECT t.created_at, 'topup', p.nickname || ' пополнил баланс · +' || round(t.amount) || ' ₽'
         || CASE WHEN t.bonus > 0 THEN ' (+' || round(t.bonus) || ' бонус)' ELSE '' END
  FROM topups t JOIN players p ON p.id = t.player_id
  WHERE t.created_at > now() - interval '11 hours';

  UPDATE players SET balance = (ARRAY[0,150,300,450,600,850,1200,1650])[1 + floor(random() * 8)::int];

  INSERT INTO audit_log (type, message) VALUES ('system', 'Демо-данные сброшены');
END $$;

SELECT reset_demo();
