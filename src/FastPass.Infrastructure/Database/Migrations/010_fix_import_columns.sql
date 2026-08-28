-- Renomeia colunas que conflitavam com palavras reservadas do MySQL
-- A tabela fp_ticket_imports foi criada com nomes antigos; esta migration corrige
ALTER TABLE fp_ticket_imports
    CHANGE COLUMN `mode` import_mode VARCHAR(30) NOT NULL DEFAULT 'ADICIONAR_ATUALIZAR';

ALTER TABLE fp_ticket_imports
    CHANGE COLUMN `status` import_status VARCHAR(20) NOT NULL DEFAULT 'pending';
