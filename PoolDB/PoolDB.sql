USE Master;
GO
DROP DATABASE pooldb;
GO
CREATE DATABASE pooldb
GO

use pooldb
-- Oprettelse af databasens tabeller --

create table [RegisteredUsers]
(
    RegisteredUserID int not null IDENTITY(1,1), 
    UserName VarChar(30) not null,
	PasswordHash BINARY(64) not null,
	RegisteredName VarChar(50),
	RegisteredEMail VarChar(100),
	CreatedAt DATETIME not null,
	LastUsedAt DATETIME not null,
	CONSTRAINT pk_RegisteredUsers_RegisteredUserID PRIMARY KEY (RegisteredUserID),
	CONSTRAINT uc_UserName UNIQUE (UserName)
);
GO

--HASHBYTES('SHA2_512', @pPassword)

create table [Tokens]
(
    TokenID int not null IDENTITY(1,1), 
    RegisteredUserID int not null,
	Token VarChar(512) not null,
	CreatedAt DATETIME not null,
	CONSTRAINT pk_Tokens_TokenID PRIMARY KEY (TokenID),
	CONSTRAINT fk_Tokens_RegisteredUserID FOREIGN KEY (RegisteredUserID) REFERENCES RegisteredUsers(RegisteredUserID)
);
GO

create table [UserLog]
(
    UserLogID int not null IDENTITY(1,1), 
    RegisteredUserID int not null,
	textLog VarChar(512) not null,
	CreatedAt DATETIME not null,
	CONSTRAINT pk_UserLog_UserLogID PRIMARY KEY (UserLogID),
	CONSTRAINT fk_UserLog_RegisteredUserID FOREIGN KEY (RegisteredUserID) REFERENCES RegisteredUsers(RegisteredUserID)
);
GO


IF EXISTS (SELECT * FROM sys.objects WHERE type = 'P' AND name = 'Sp_Create_User')
DROP PROCEDURE Sp_Create_User
GO

CREATE PROCEDURE Sp_Create_User
    @pUserName NVARCHAR(30), 
    @pPassword NVARCHAR(50),
    @responseMessage NVARCHAR(250) OUTPUT
AS
BEGIN
    SET NOCOUNT ON

    BEGIN TRY

        INSERT INTO RegisteredUsers (UserName, PasswordHash, CreatedAt, LastUsedAt)
        VALUES(@pUserName, HASHBYTES('SHA2_512', @pPassword), GETUTCDATE(),GETUTCDATE())

        SET @responseMessage='Success'

    END TRY
    BEGIN CATCH
        SET @responseMessage=ERROR_MESSAGE() 
    END CATCH

END

GO

DECLARE @responseMessage NVARCHAR(250)

EXEC Sp_Create_User
          @pUserName = N'timrip',
          @pPassword = N'ug2-gj-8',
          @responseMessage=@responseMessage OUTPUT

select @responseMessage

GO

IF EXISTS (SELECT * FROM sys.objects WHERE type = 'P' AND name = 'SP_LoginUser')
DROP PROCEDURE SP_LoginUser
GO

CREATE PROCEDURE SP_LoginUser
    @pUserName NVARCHAR(30),
    @pPassword NVARCHAR(50),
    @responseMessage NVARCHAR(250)='' OUTPUT,
	@userID INT=-1 OUTPUT
AS
BEGIN

    SET NOCOUNT ON

    IF EXISTS (SELECT TOP 1 RegisteredUserID FROM RegisteredUsers WHERE UserName=@pUserName)
    BEGIN
        SET @userID=(SELECT RegisteredUserID FROM RegisteredUsers WHERE UserName=@pUserName AND PasswordHash=HASHBYTES('SHA2_512', @pPassword))

       IF(@userID IS NULL)
           SET @responseMessage='Incorrect password'
       ELSE 
	   BEGIN
		   UPDATE RegisteredUsers SET LastUsedAt = GETUTCDATE() WHERE RegisteredUserID = @userID
           SET @responseMessage='User successfully logged in'
	   END
    END
    ELSE
	BEGIN
       SET @responseMessage='Invalid login'
	   SET @userID = -1
    END

END

GO

IF EXISTS (SELECT * FROM sys.objects WHERE type = 'P' AND name = 'SP_SaveToken')
DROP PROCEDURE SP_SaveToken
GO

CREATE PROCEDURE SP_SaveToken
	@userID int,
	@token VARCHAR(512)
AS
BEGIN

    SET NOCOUNT ON
	INSERT INTO Tokens (RegisteredUserID, Token, CreatedAt) VALUES (@userID, @token, GETUTCDATE())
END

GO

DECLARE @userID INT
DECLARE @responseMessage NVARCHAR(250)

EXEC SP_LoginUser
          @pUserName = 'timrip',
          @pPassword = 'ug2-gj-8',
          @responseMessage=@responseMessage OUTPUT,
		  @userID=@userID OUTPUT

select @responseMessage, @userID

GO

IF EXISTS (SELECT * FROM sys.objects WHERE type = 'P' AND name = 'SP_UserLog')
DROP PROCEDURE SP_UserLog
GO

CREATE PROCEDURE SP_UserLog
	@logtext VARCHAR(512),
	@token VARCHAR(512)
AS
BEGIN
    
    SET NOCOUNT ON
	INSERT INTO UserLog (RegisteredUserID, textLog, CreatedAt)
	Select RegisteredUserID, @logtext, GETUTCDATE() from Tokens where Token = @token
	
END

GO

CREATE TABLE [Facility] (
  [Id] int CONSTRAINT PK_Facility PRIMARY KEY IDENTITY(1, 1),
  [Name] nvarchar(255),
  [Description] nvarchar(255),
  [TeamId] int
)
GO

CREATE TABLE [Schedule] (
  [Id] int CONSTRAINT PK_Schedule PRIMARY KEY IDENTITY(1, 1),
  [FacilityId] int,
  [FromTime] DATETIME,
  [ToTime] DATETIME
)
GO

CREATE TABLE [Player] (
  [Id] int CONSTRAINT PK_Player PRIMARY KEY IDENTITY(1, 1),
  [RegisteredUserID] int,
  [Name] nvarchar(255) not null,
  [ParentPlayerId] int,
  [CreatedAt] DATETIME,
  [LastUsedAt] DATETIME
)
GO

CREATE TABLE [Seat] (
  [Id] int CONSTRAINT PK_Seat PRIMARY KEY IDENTITY(1, 1),
  [ParentSeatId] int,
  [PlayerId] int,
  [MatchId] int not null,
  [AutoSelectMatchId] int,
  [AutoSelectlPlace] int,
  [ResultMatchPlace] int,
  [ResultPoints] int
)
GO

CREATE TABLE [PlayStyle] (
  [Id] int CONSTRAINT PK_PlayStyle PRIMARY KEY IDENTITY(1, 1),
  [Description] nvarchar(255),
)
GO

CREATE TABLE [MatchRules] (
  [Id] int CONSTRAINT PK_MatchRules PRIMARY KEY IDENTITY(1, 1),
  [PlayStyleId] int not null,
  [Description] nvarchar(255),
  [PlayFrom] int,
  [PlayTo] int
)
GO

CREATE TABLE [Match] (
  [Id] int CONSTRAINT PK_Match PRIMARY KEY IDENTITY(1, 1),
  [ScheduleId] int,
  [MatchRulesId] int,
  [ParentMatchId] int,
  [IndividualMatch] bit not null,
  [Name] nvarchar(255) not null
)
GO

CREATE TABLE [AdminPoint] (
  [PlayerId] int,
  [MatchId] int,
  [Point] int,
  [PointType] int,
  [Note] nvarchar(255)
)
GO

CREATE TABLE [Tournament] (
  [Id] int CONSTRAINT PK_Tournament PRIMARY KEY IDENTITY(1, 1),
  [MatchId] int not null,
  [Admin] int not null,
  [default] bit not null,
  [Description] nvarchar(255),
  [name] nvarchar(255) not null,
  [FormData] nvarchar(max)
)
GO

ALTER TABLE [Schedule] ADD CONSTRAINT FK_Schedule_FacilityId FOREIGN KEY ([FacilityId]) REFERENCES [Facility] ([Id])
GO

ALTER TABLE [Player] ADD CONSTRAINT FK_Player_RegisteredUserID FOREIGN KEY ([RegisteredUserID]) REFERENCES [RegisteredUsers] ([RegisteredUserID])
GO

ALTER TABLE [Player] ADD CONSTRAINT FK_Player_ParentPlayerId FOREIGN KEY ([ParentPlayerId]) REFERENCES [Player] ([Id])
GO

ALTER TABLE [Seat] ADD CONSTRAINT FK_Seat_ParentSeatId FOREIGN KEY ([ParentSeatId]) REFERENCES [Seat] ([Id])
GO

ALTER TABLE [Seat] ADD CONSTRAINT FK_Seat_PlayerId FOREIGN KEY ([PlayerId]) REFERENCES [Player] ([Id])
GO

ALTER TABLE [Seat] ADD CONSTRAINT FK_Seat_AutoSelectMatchId FOREIGN KEY ([AutoSelectMatchId]) REFERENCES [Match] ([Id])
GO

ALTER TABLE [Seat] ADD CONSTRAINT FK_Seat_MatchId FOREIGN KEY ([MatchId]) REFERENCES [Match] ([Id])
GO
--Addwd
ALTER TABLE [MatchRules] ADD CONSTRAINT FK_MatchRules_PlayStyleId FOREIGN KEY ([PlayStyleId]) REFERENCES [PlayStyle] ([Id])
GO

ALTER TABLE [Match] ADD CONSTRAINT FK_Match_ScheduleId FOREIGN KEY ([ScheduleId]) REFERENCES [Schedule] ([Id])
GO

ALTER TABLE [Match] ADD CONSTRAINT FK_Match_MatchRulesId FOREIGN KEY ([MatchRulesId]) REFERENCES [MatchRules] ([Id])
GO

ALTER TABLE [Match] ADD CONSTRAINT FK_Match_ParentMatchId FOREIGN KEY ([ParentMatchId]) REFERENCES [Match] ([Id])
GO

ALTER TABLE [AdminPoint] ADD CONSTRAINT FK_AdminPoint_PlayerId FOREIGN KEY ([PlayerId]) REFERENCES [Player] ([Id])
GO

ALTER TABLE [AdminPoint] ADD CONSTRAINT FK_AdminPoint_MatchId FOREIGN KEY ([MatchId]) REFERENCES [Match] ([Id])
GO

ALTER TABLE [Tournament] ADD CONSTRAINT FK_Tournament_MatchId FOREIGN KEY ([MatchId]) REFERENCES [Match] ([Id])
GO

ALTER TABLE [Tournament] ADD CONSTRAINT FK_Tournament_Admin FOREIGN KEY ([Admin]) REFERENCES [RegisteredUsers] ([RegisteredUserID])
GO


IF EXISTS (SELECT * FROM sys.objects WHERE type = 'P' AND name = 'SP_CreateMatch')
DROP PROCEDURE SP_CreateMatch
GO

CREATE PROCEDURE SP_CreateMatch
    @pName NVARCHAR(255),
    @pParentMachId INT = NULL,
	@pMatchRulesId INT = NULL,
	@pInduvidual BIT,
	@MatchId INT=-1 OUTPUT,
	@responseMessage NVARCHAR(250) OUTPUT
AS
BEGIN

    SET NOCOUNT ON
	
	BEGIN TRY
		INSERT INTO Match(ParentMatchId, MatchRulesId, IndividualMatch, [Name]) VALUES (@pParentMachId, @pMatchRulesId, @pInduvidual, @pName)
		SET @MatchId = SCOPE_IDENTITY();
		SET @responseMessage = 'done';
	END TRY
	BEGIN CATCH
		SET @MatchId = -1;
		SET @responseMessage = ERROR_MESSAGE();
	END CATCH

END

GO

IF EXISTS (SELECT * FROM sys.objects WHERE type = 'P' AND name = 'SP_SetParentMatchId')
DROP PROCEDURE SP_SetParentMatchId
GO

CREATE PROCEDURE SP_SetParentMatchId
	@pMatchId INT,
    @pParentMachId INT,
	@Result INT=-1 OUTPUT
AS
BEGIN

    SET NOCOUNT ON

	UPDATE Match
	SET ParentMatchId = @pParentMachId
	WHERE Id = @pMatchId;
			

	IF (@@ROWCOUNT > 0)
	BEGIN
		SET @Result = @@ERROR
	END
	ELSE 
	BEGIN
		SET @Result = @@ERROR
	END

END

GO

IF EXISTS (SELECT * FROM sys.objects WHERE type = 'P' AND name = 'SP_CreatePlayer')
DROP PROCEDURE SP_CreatePlayer
GO

CREATE PROCEDURE SP_CreatePlayer
	@pRegisteredUserID INT = NULL,
	@pName NVARCHAR(255),
	@pParentPlayerId INT = NULL,
	@PlayerId INT=-1 OUTPUT,
	@responseMessage NVARCHAR(250) OUTPUT
AS
BEGIN

    SET NOCOUNT ON
	
	BEGIN TRY
		INSERT INTO Player(RegisteredUserID, Name, ParentPlayerId, CreatedAt, LastUsedAt) VALUES (@pRegisteredUserID,@pName,@pParentPlayerId,GETUTCDATE(),GETUTCDATE())
		SET @PlayerId = SCOPE_IDENTITY(); 
		SET @responseMessage = 'done';
	END TRY
	BEGIN CATCH
		SET @PlayerId = -1;
		SET @responseMessage = ERROR_MESSAGE();
	END CATCH
END
GO

IF EXISTS (SELECT * FROM sys.objects WHERE type = 'P' AND name = 'SP_CreateAutoSelectSeat')
DROP PROCEDURE SP_CreateAutoSelectSeat
GO


IF EXISTS (SELECT * FROM sys.objects WHERE type = 'P' AND name = 'SP_CreateSeat')
DROP PROCEDURE SP_CreateSeat
GO

CREATE PROCEDURE SP_CreateSeat
	@pParentSeatId INT = NULL,
	@pPlayerId INT = NULL,
	@pMatchId INT,
	@pAutoSelectMatchId INT = NULL,
	@pAutoSelectPlace INT = NULL,
	@SeatId INT=-1 OUTPUT,
	@responseMessage NVARCHAR(250) OUTPUT
AS
BEGIN

    SET NOCOUNT ON
	
	BEGIN TRY
		INSERT INTO Seat(ParentSeatId,PlayerId, MatchId, AutoSelectMatchId, AutoSelectlPlace) VALUES (@pParentSeatId, @pPlayerId, @pMatchId,@pAutoSelectMatchId, @pAutoSelectPlace)
		SET @SeatId = SCOPE_IDENTITY(); 
		SET @responseMessage = 'done';
	END TRY
	BEGIN CATCH
		SET @SeatId = -1;
		SET @responseMessage = ERROR_MESSAGE();
	END CATCH
END

GO

IF EXISTS (SELECT * FROM sys.objects WHERE type = 'P' AND name = 'SP_CreateTournament')
DROP PROCEDURE SP_CreateTournament
GO

CREATE PROCEDURE SP_CreateTournament

	@pMatchId INT,
	@pAdmin INT,
	@pDefault BIT,
	@pDescription NVARCHAR(255) = NULL,
	@pName NVARCHAR(255),
	@pFormData NVARCHAR(MAX) = NULL,
	@TurnamentId INT=-1 OUTPUT,
	@responseMessage NVARCHAR(250) OUTPUT
AS
BEGIN

    SET NOCOUNT ON
	
	BEGIN TRY
		INSERT INTO Tournament(MatchId,[Admin], [default], [Description], [name], FormData) VALUES (@pMatchId, @pAdmin, @pDefault,@pDescription, @pName, @pFormData)
		SET @TurnamentId = SCOPE_IDENTITY(); 
		SET @responseMessage = 'done';
	END TRY
	BEGIN CATCH
		SET @TurnamentId = -1;
		SET @responseMessage = ERROR_MESSAGE();
	END CATCH
END

GO

IF EXISTS (SELECT * FROM sys.objects WHERE type = 'P' AND name = 'SP_SetSeatAutoPlace')
DROP PROCEDURE SP_SetSeatAutoPlace
GO

CREATE PROCEDURE SP_SetSeatAutoPlace
	@pSeatId INT,
    @pAutoSelectMatchId INT,
	@pAutoSelectlPlace INT,
	@Result INT=-1 OUTPUT
AS
BEGIN

    SET NOCOUNT ON

	UPDATE Seat
	SET AutoSelectMatchId = @pAutoSelectMatchId, AutoSelectlPlace = @pAutoSelectlPlace
	WHERE Id = @pSeatId;
			

	IF (@@ROWCOUNT > 0)
	BEGIN
		SET @Result = @@ERROR
	END
	ELSE 
	BEGIN
		SET @Result = @@ERROR
	END

END

GO

IF EXISTS (SELECT * FROM sys.objects WHERE type = 'P' AND name = 'SP_SetPlayerOnSeat')
DROP PROCEDURE SP_SetPlayerOnSeat
GO

CREATE PROCEDURE SP_SetPlayerOnSeat
	@pSeatId INT,
    @pPlayerId INT,
	@Result INT=-1 OUTPUT
AS
BEGIN

    SET NOCOUNT ON

	UPDATE Seat
	SET PlayerId = @pPlayerId
	WHERE Id = @pSeatId or ParentSeatId = @pSeatId 
			

	IF (@@ROWCOUNT > 0)
	BEGIN
		SET @Result = @@ERROR
	END
	ELSE 
	BEGIN
		SET @Result = @@ERROR
	END

END

GO

IF EXISTS (SELECT * FROM sys.objects WHERE type = 'P' AND name = 'SP_GetUserFromToken')
DROP PROCEDURE SP_GetUserFromToken
GO

CREATE PROCEDURE SP_GetUserFromToken

	@token VARCHAR(512),
	@UserId INT=-1 OUTPUT
AS
BEGIN

    SET NOCOUNT ON
	
	BEGIN TRY
		SELECT @UserId = RegisteredUserID from Tokens where  Token = @token
	END TRY
	BEGIN CATCH
		SET @UserId = -1;
	END CATCH
END

GO

--We add to 3 different playstyles
INSERT INTO PlayStyle (Description)
VALUES ('Alle mod Alle');
GO

INSERT INTO PlayStyle (Description)
VALUES ('Swiss');
GO

INSERT INTO PlayStyle (Description)
VALUES ('Alle i en kamp');
GO

IF EXISTS (SELECT * FROM sys.objects WHERE type = 'P' AND name = 'SP_CreateMatchRule')
DROP PROCEDURE SP_CreateMatchRule
GO

CREATE PROCEDURE SP_CreateMatchRule
    @pPlayStyleId INT,
	@pPlayFrom INT,
	@pPlayTo INT,
	@pDescription NVARCHAR(255) = NULL,
	@MatchRuleId INT=-1 OUTPUT,
	@responseMessage NVARCHAR(250) OUTPUT
AS
BEGIN

    SET NOCOUNT ON
	
	BEGIN TRY
		INSERT INTO MatchRules(PlayStyleId,PlayFrom,PlayTo,Description) VALUES (@pPlayStyleId, @pPlayFrom, @pPlayTo, @pDescription)
		SET @MatchRuleId = SCOPE_IDENTITY();
		SET @responseMessage = 'done';
	END TRY
	BEGIN CATCH
		SET @MatchRuleId = -1;
		SET @responseMessage = ERROR_MESSAGE();
	END CATCH

END

GO

IF EXISTS (SELECT * FROM sys.objects WHERE type = 'P' AND name = 'SP_GetMatchesFromTournament')
    DROP PROCEDURE dbo.SP_GetMatchesFromTournament;
GO

CREATE PROCEDURE dbo.SP_GetMatchesFromTournament
    @pTournamentId INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @TournamentMatchId INT;
    SELECT @TournamentMatchId = MatchId
    FROM Tournament
    WHERE Id = @pTournamentId;

    ;WITH parentmatches AS
    (
        -- Root (anchor)
        SELECT
            0 AS [level],
            CAST(NULL AS INT) AS ParentMatchId,       -- ✅ computed parent for UI tree
            M.Id AS MatchId,
            M.ParentMatchId AS RealParentMatchId,     -- (optional) actual DB parent
            M.Name AS MatchName,
            CAST('|' + CAST(M.Id AS VARCHAR(20)) + '|' AS VARCHAR(MAX)) AS [path]  -- cycle guard
        FROM [Match] M
        WHERE M.Id = @TournamentMatchId

        UNION ALL

        -- Children
        SELECT
            PM.[level] + 1 AS [level],
            PM.MatchId AS ParentMatchId,              -- ✅ computed parent = the node we came from
            M.Id AS MatchId,
            M.ParentMatchId AS RealParentMatchId,
            M.Name AS MatchName,
            CAST(PM.[path] + CAST(M.Id AS VARCHAR(20)) + '|' AS VARCHAR(MAX)) AS [path]
        FROM [Match] M
        INNER JOIN parentmatches PM
            ON M.ParentMatchId = PM.MatchId
        WHERE
            M.Id <> @TournamentMatchId
            AND CHARINDEX('|' + CAST(M.Id AS VARCHAR(20)) + '|', PM.[path]) = 0   -- ✅ prevent cycles
    )
    SELECT
        PM.[level],
        PM.ParentMatchId,            -- ✅ this is the one your HTML tree should use
        PM.MatchId,
        PM.MatchName,
        PL.Name AS PlayerName

        -- Uncomment if you want to see the "real" parent too:
        -- , PM.RealParentMatchId

    FROM parentmatches PM
    LEFT JOIN Seat S
        ON PM.MatchId = S.MatchId
    LEFT JOIN Player PL
        ON S.PlayerId = PL.Id
    ORDER BY PM.[level], PM.ParentMatchId, PM.MatchId;
END
GO
/*
Adds ownership to Facility so tournament administrators can create their own
venues and assign each pool to one of them.

This is a migration for an existing database. It does NOT drop any tables.
*/

IF COL_LENGTH('dbo.Facility', 'AdminId') IS NULL
BEGIN
    ALTER TABLE dbo.Facility
    ADD AdminId INT NULL;
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = 'FK_Facility_AdminId'
)
BEGIN
    ALTER TABLE dbo.Facility
    ADD CONSTRAINT FK_Facility_AdminId
        FOREIGN KEY (AdminId)
        REFERENCES dbo.RegisteredUsers(RegisteredUserID);
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_Facility_AdminId'
      AND object_id = OBJECT_ID('dbo.Facility')
)
BEGIN
    CREATE INDEX IX_Facility_AdminId
        ON dbo.Facility(AdminId);
END
GO

/*
Player registration migration
2026-09-30

Safe migration for an EXISTING pooldb database.
It does not drop the database or existing tournament data.

Run this file once against pooldb before using the player app.
*/

USE pooldb;
GO

IF OBJECT_ID('dbo.TournamentRegistration', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.TournamentRegistration
    (
        Id INT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_TournamentRegistration PRIMARY KEY,
        TournamentId INT NOT NULL,
        RegisteredUserId INT NOT NULL,
        PlayerId INT NULL,
        OriginalPlayerName NVARCHAR(255) NULL,
        Status NVARCHAR(20) NOT NULL
            CONSTRAINT DF_TournamentRegistration_Status DEFAULT ('registered'),
        RegisteredAt DATETIME NOT NULL
            CONSTRAINT DF_TournamentRegistration_RegisteredAt DEFAULT (GETUTCDATE()),
        UpdatedAt DATETIME NOT NULL
            CONSTRAINT DF_TournamentRegistration_UpdatedAt DEFAULT (GETUTCDATE()),

        CONSTRAINT FK_TournamentRegistration_Tournament
            FOREIGN KEY (TournamentId) REFERENCES dbo.Tournament(Id),
        CONSTRAINT FK_TournamentRegistration_User
            FOREIGN KEY (RegisteredUserId) REFERENCES dbo.RegisteredUsers(RegisteredUserID),
        CONSTRAINT FK_TournamentRegistration_Player
            FOREIGN KEY (PlayerId) REFERENCES dbo.Player(Id),
        CONSTRAINT UQ_TournamentRegistration_Tournament_User
            UNIQUE (TournamentId, RegisteredUserId),
        CONSTRAINT CK_TournamentRegistration_Status
            CHECK (Status IN ('registered', 'cancelled'))
    );
END
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_TournamentRegistration_PlayerId'
      AND object_id = OBJECT_ID('dbo.TournamentRegistration')
)
BEGIN
    CREATE INDEX IX_TournamentRegistration_PlayerId
        ON dbo.TournamentRegistration(PlayerId);
END
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_TournamentRegistration_Tournament_Status'
      AND object_id = OBJECT_ID('dbo.TournamentRegistration')
)
BEGIN
    CREATE INDEX IX_TournamentRegistration_Tournament_Status
        ON dbo.TournamentRegistration(TournamentId, Status);
END
GO

/*
Adds public/private tournament visibility and an administrator-generated join code.
Existing tournaments remain public. This migration does not delete any data.
*/

IF COL_LENGTH('dbo.Tournament', 'IsPrivate') IS NULL
BEGIN
    ALTER TABLE dbo.Tournament
    ADD IsPrivate bit NOT NULL
        CONSTRAINT DF_Tournament_IsPrivate DEFAULT (0) WITH VALUES;
END
GO

IF COL_LENGTH('dbo.Tournament', 'JoinCode') IS NULL
BEGIN
    ALTER TABLE dbo.Tournament
    ADD JoinCode nvarchar(20) NULL;
END
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = 'UX_Tournament_JoinCode'
      AND object_id = OBJECT_ID('dbo.Tournament')
)
BEGIN
    CREATE UNIQUE INDEX UX_Tournament_JoinCode
        ON dbo.Tournament(JoinCode)
        WHERE JoinCode IS NOT NULL;
END
GO


IF OBJECT_ID('dbo.TournamentMatchEditor', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.TournamentMatchEditor
    (
        TournamentId INT NOT NULL,
        RegisteredUserId INT NOT NULL,
        AddedByUserId INT NOT NULL,
        CreatedAt DATETIME NOT NULL
            CONSTRAINT DF_TournamentMatchEditor_CreatedAt DEFAULT (GETUTCDATE()),

        CONSTRAINT PK_TournamentMatchEditor
            PRIMARY KEY (TournamentId, RegisteredUserId),

        CONSTRAINT FK_TournamentMatchEditor_Tournament
            FOREIGN KEY (TournamentId)
            REFERENCES dbo.Tournament(Id)
            ON DELETE CASCADE,

        CONSTRAINT FK_TournamentMatchEditor_User
            FOREIGN KEY (RegisteredUserId)
            REFERENCES dbo.RegisteredUsers(RegisteredUserID),

        CONSTRAINT FK_TournamentMatchEditor_AddedBy
            FOREIGN KEY (AddedByUserId)
            REFERENCES dbo.RegisteredUsers(RegisteredUserID)
    );
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_TournamentMatchEditor_RegisteredUserId'
      AND object_id = OBJECT_ID('dbo.TournamentMatchEditor')
)
BEGIN
    CREATE INDEX IX_TournamentMatchEditor_RegisteredUserId
        ON dbo.TournamentMatchEditor(RegisteredUserId, TournamentId);
END
GO
