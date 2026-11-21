-- One-time local MySQL setup for BiteBox. Replace <your-password> before running:
--   mysql -u root -p < docs/setup-local-db.sql
CREATE DATABASE IF NOT EXISTS bitebox_db CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;
CREATE DATABASE IF NOT EXISTS bitebox_test CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;
CREATE USER IF NOT EXISTS 'bitebox_app'@'localhost' IDENTIFIED BY '<your-password>';
-- Access is limited to databases whose names start with "bitebox_".
GRANT ALL PRIVILEGES ON `bitebox\_%`.* TO 'bitebox_app'@'localhost';
FLUSH PRIVILEGES;
