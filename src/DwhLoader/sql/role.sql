-- スーパーユーザが、jv のテーブルを作ったあとに実行する。
-- パスワードは \password keiba_loader で決め、このファイルには書かない。

CREATE ROLE keiba_loader LOGIN;
GRANT CONNECT ON DATABASE keiba TO keiba_loader;
GRANT USAGE ON SCHEMA jv TO keiba_loader;
GRANT SELECT, INSERT ON ALL TABLES IN SCHEMA jv TO keiba_loader;
GRANT DELETE ON TABLE jv.ob14_active TO keiba_loader;
ALTER DEFAULT PRIVILEGES IN SCHEMA jv GRANT SELECT, INSERT ON TABLES TO keiba_loader;
