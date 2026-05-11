CREATE TABLE IF NOT EXISTS `__BraikovNotificationsMigrationsHistory` (
    `MigrationId` varchar(150) CHARACTER SET utf8mb4 NOT NULL,
    `ProductVersion` varchar(32) CHARACTER SET utf8mb4 NOT NULL,
    CONSTRAINT `PK___BraikovNotificationsMigrationsHistory` PRIMARY KEY (`MigrationId`)
) CHARACTER SET=utf8mb4;

START TRANSACTION;
DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__BraikovNotificationsMigrationsHistory` WHERE `MigrationId` = '20260506151647_InitialNotificationsSchema') THEN

    ALTER DATABASE CHARACTER SET utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__BraikovNotificationsMigrationsHistory` WHERE `MigrationId` = '20260506151647_InitialNotificationsSchema') THEN

    CREATE TABLE `Notifications` (
        `Id` char(36) COLLATE ascii_general_ci NOT NULL,
        `RecipientId` varchar(64) CHARACTER SET utf8mb4 NOT NULL,
        `TypeKey` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
        `Title` varchar(256) CHARACTER SET utf8mb4 NOT NULL,
        `Message` varchar(2000) CHARACTER SET utf8mb4 NOT NULL,
        `PayloadJson` longtext CHARACTER SET utf8mb4 NULL,
        `Culture` varchar(16) CHARACTER SET utf8mb4 NULL,
        `Source` varchar(128) CHARACTER SET utf8mb4 NULL,
        `PreferencePolicy` int NOT NULL,
        `IdempotencyKey` varchar(256) CHARACTER SET utf8mb4 NULL,
        `CorrelationId` varchar(128) CHARACTER SET utf8mb4 NULL,
        `PayloadHash` varchar(128) CHARACTER SET utf8mb4 NULL,
        `CreateInboxRecord` tinyint(1) NOT NULL,
        `CreatedAt` datetime(6) NOT NULL,
        `IsRead` tinyint(1) NOT NULL,
        `ReadAt` datetime(6) NULL,
        CONSTRAINT `PK_Notifications` PRIMARY KEY (`Id`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__BraikovNotificationsMigrationsHistory` WHERE `MigrationId` = '20260506151647_InitialNotificationsSchema') THEN

    CREATE TABLE `UserNotificationPreferences` (
        `Id` char(36) COLLATE ascii_general_ci NOT NULL,
        `RecipientId` varchar(64) CHARACTER SET utf8mb4 NOT NULL,
        `NotificationTypeKey` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
        `EnabledChannelKeys` varchar(512) CHARACTER SET utf8mb4 NOT NULL,
        `CreatedAt` datetime(6) NOT NULL,
        `UpdatedAt` datetime(6) NULL,
        CONSTRAINT `PK_UserNotificationPreferences` PRIMARY KEY (`Id`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__BraikovNotificationsMigrationsHistory` WHERE `MigrationId` = '20260506151647_InitialNotificationsSchema') THEN

    CREATE TABLE `NotificationDeliveries` (
        `Id` char(36) COLLATE ascii_general_ci NOT NULL,
        `NotificationId` char(36) COLLATE ascii_general_ci NOT NULL,
        `RecipientId` varchar(64) CHARACTER SET utf8mb4 NOT NULL,
        `ChannelKey` varchar(64) CHARACTER SET utf8mb4 NOT NULL,
        `Status` int NOT NULL,
        `ProviderKey` varchar(64) CHARACTER SET utf8mb4 NULL,
        `ProviderMessageId` varchar(256) CHARACTER SET utf8mb4 NULL,
        `AttemptCount` int NOT NULL,
        `LastAttemptAt` datetime(6) NULL,
        `SentAt` datetime(6) NULL,
        `OpenedAt` datetime(6) NULL,
        `ClickedAt` datetime(6) NULL,
        `NextRetryAt` datetime(6) NULL,
        `SkippedReason` varchar(128) CHARACTER SET utf8mb4 NULL,
        `ErrorCode` varchar(128) CHARACTER SET utf8mb4 NULL,
        `ErrorMessage` varchar(2000) CHARACTER SET utf8mb4 NULL,
        `CreatedAt` datetime(6) NOT NULL,
        CONSTRAINT `PK_NotificationDeliveries` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_NotificationDeliveries_Notifications_NotificationId` FOREIGN KEY (`NotificationId`) REFERENCES `Notifications` (`Id`) ON DELETE CASCADE
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__BraikovNotificationsMigrationsHistory` WHERE `MigrationId` = '20260506151647_InitialNotificationsSchema') THEN

    CREATE INDEX `IX_NotificationDeliveries_ChannelKey_Status` ON `NotificationDeliveries` (`ChannelKey`, `Status`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__BraikovNotificationsMigrationsHistory` WHERE `MigrationId` = '20260506151647_InitialNotificationsSchema') THEN

    CREATE INDEX `IX_NotificationDeliveries_NotificationId` ON `NotificationDeliveries` (`NotificationId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__BraikovNotificationsMigrationsHistory` WHERE `MigrationId` = '20260506151647_InitialNotificationsSchema') THEN

    CREATE INDEX `IX_NotificationDeliveries_RecipientId_CreatedAt` ON `NotificationDeliveries` (`RecipientId`, `CreatedAt`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__BraikovNotificationsMigrationsHistory` WHERE `MigrationId` = '20260506151647_InitialNotificationsSchema') THEN

    CREATE INDEX `IX_NotificationDeliveries_Status_NextRetryAt` ON `NotificationDeliveries` (`Status`, `NextRetryAt`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__BraikovNotificationsMigrationsHistory` WHERE `MigrationId` = '20260506151647_InitialNotificationsSchema') THEN

    CREATE INDEX `IX_Notifications_RecipientId_CreatedAt` ON `Notifications` (`RecipientId`, `CreatedAt`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__BraikovNotificationsMigrationsHistory` WHERE `MigrationId` = '20260506151647_InitialNotificationsSchema') THEN

    CREATE UNIQUE INDEX `IX_Notifications_RecipientId_TypeKey_IdempotencyKey` ON `Notifications` (`RecipientId`, `TypeKey`, `IdempotencyKey`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__BraikovNotificationsMigrationsHistory` WHERE `MigrationId` = '20260506151647_InitialNotificationsSchema') THEN

    CREATE UNIQUE INDEX `IX_UserNotificationPreferences_RecipientId_NotificationTypeKey` ON `UserNotificationPreferences` (`RecipientId`, `NotificationTypeKey`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__BraikovNotificationsMigrationsHistory` WHERE `MigrationId` = '20260506151647_InitialNotificationsSchema') THEN

    INSERT INTO `__BraikovNotificationsMigrationsHistory` (`MigrationId`, `ProductVersion`)
    VALUES ('20260506151647_InitialNotificationsSchema', '9.0.0');

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

COMMIT;

