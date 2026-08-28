-- Torna employee_code opcional para suportar colaboradores avulsos sem matrícula
ALTER TABLE fp_staff_members
    CHANGE COLUMN employee_code employee_code VARCHAR(120) NULL DEFAULT NULL;

-- Atualiza o índice único para ignorar NULLs (MySQL ignora NULLs em índices únicos por padrão)
-- Nenhuma ação adicional necessária — NULL != NULL no MySQL
