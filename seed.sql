INSERT INTO "Competitions" ("Id", "CompetitionId", "Title", "Description", "GameModeType", "OwnerId", "StartTime", "EndTime", "Status")
VALUES ('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1', 'Test CTF Competition', 'A demo competition for QA', 0, '1a24e8a7-548c-48df-9635-51c8a932c9c3', NOW(), NOW() + INTERVAL '1 day', 0);

INSERT INTO "Teams" ("Id", "CompetitionId", "Name", "CaptainId", "CreatedAt")
VALUES ('cccccccc-cccc-cccc-cccc-ccccccccccc1', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1', 'TestQA Team', '1a24e8a7-548c-48df-9635-51c8a932c9c3', NOW());

INSERT INTO "TeamMembers" ("Id", "TeamId", "UserId", "Role", "JoinedAt")
VALUES (gen_random_uuid(), 'cccccccc-cccc-cccc-cccc-ccccccccccc1', '1a24e8a7-548c-48df-9635-51c8a932c9c3', 0, NOW());

INSERT INTO "Challenges" ("Id", "CompetitionId", "Title", "Description", "TypeId", "PointsConfig_InitialPoints", "PointsConfig_MinimumPoints", "PointsConfig_DecayFactor", "PointsConfig_DecayFunction", "AttachmentUrl", "ContainerImage", "FlagSecret", "CreatedAt")
VALUES 
('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb1', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1', 'Web Easy', 'A simple web challenge', 'web', 1000, 100, 450, 'logarithmic', null, null, 'flag{web_easy}', NOW()),
('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb2', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1', 'Pwn Medium', 'A medium pwn challenge', 'pwn', 1000, 100, 450, 'logarithmic', null, null, 'flag{pwn_medium}', NOW());
