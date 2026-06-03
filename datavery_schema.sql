SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- Employees table
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Employees' AND xtype='U')
CREATE TABLE Employees (
    id          INT             PRIMARY KEY,
    name        NVARCHAR(100)   NOT NULL,
    code        NVARCHAR(20)    NOT NULL,
    tag         NVARCHAR(50)    NOT NULL,
    created_at  DATETIME2       NULL,
    updated_at  DATETIME2       NULL,
    deleted_at  DATETIME2       NULL
);

-- Workstations table
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Workstations' AND xtype='U')
CREATE TABLE Workstations (
    id              INT             PRIMARY KEY,
    name            NVARCHAR(150)   NOT NULL,
    warehouse_id    INT             NOT NULL,
    tag             NVARCHAR(20)    NOT NULL,
    created_at      DATETIME2       NULL,
    deleted_at      DATETIME2       NULL
);

-- Items table
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Items' AND xtype='U')
CREATE TABLE Items (
    id              INT             PRIMARY KEY,
    tag             NVARCHAR(50)    NOT NULL,
    clothing_type   INT             NOT NULL,
    workstation_id  INT             NOT NULL REFERENCES Workstations(id),
    employee_id     INT             NOT NULL REFERENCES Employees(id)
);
