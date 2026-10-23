USE DemoDataBase;
GO

DELETE FROM dbo.UserRoles;
DELETE FROM dbo.Users;
GO

INSERT INTO dbo.Users (UserName, PasswordHash, DisplayName, IsActive) VALUES
('Praveen', '100000.Jzy103WsxBVX/BubViv+nQ==.IFOWqjnjfsmaOCTnRLYD5syJFFbgpqIoVm7Q3yHUGb4=', 'Praveen Kumar', 1),
('Adjuster', '100000.pinhw0dGGUUgtxuESouU1w==.9Irmx1vXZdtzfSHEsXR1CNPGbhEqRK3mw32hgcVqIyc=', 'Claims Adjuster', 1);

INSERT INTO dbo.UserRoles (UserId, RoleId)
SELECT u.UserId, r.RoleId FROM dbo.Users u CROSS JOIN dbo.Roles r
WHERE u.UserName = 'Praveen' AND r.RoleName = 'Admin';

INSERT INTO dbo.UserRoles (UserId, RoleId)
SELECT u.UserId, r.RoleId FROM dbo.Users u CROSS JOIN dbo.Roles r
WHERE u.UserName = 'Adjuster' AND r.RoleName = 'ClaimsAdjuster';
GO
