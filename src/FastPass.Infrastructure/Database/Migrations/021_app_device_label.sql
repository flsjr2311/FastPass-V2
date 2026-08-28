-- Identificação do aparelho/instalação do APP que realizou a validação.
-- O celular do operador não é um dispositivo físico cadastrado (fp_devices);
-- guardamos aqui um rótulo amigável + o id de instalação gerado pelo app.
ALTER TABLE fp_access_attempts
    ADD COLUMN app_device_label VARCHAR(128) NULL COMMENT 'Nome amigável do aparelho do app (ex: Samsung do João)' AFTER device_id;

ALTER TABLE fp_access_attempts
    ADD COLUMN app_device_install_id VARCHAR(64) NULL COMMENT 'UUID de instalação do app (persistido no aparelho)' AFTER app_device_label;

CREATE INDEX ix_fp_access_attempts_app_install ON fp_access_attempts (app_device_install_id);
