import type { CompetitionEventKind } from '@/api/competitionEventApi'

export const competitionEventKindEntries = [
  [0, 'competitionCreated'],
  [1, 'competitionUpdated'],
  [2, 'competitionDeleted'],
  [3, 'competitionLifecycleChanged'],
  [4, 'leaderboardVisibilityChanged'],
  [5, 'challengeCreated'],
  [6, 'challengeUpdated'],
  [7, 'challengePublished'],
  [8, 'challengeUnpublished'],
  [9, 'challengeDeleted'],
  [10, 'hintPublished'],
  [11, 'hintUnlocked'],
  [12, 'teamRegistered'],
  [13, 'teamRegistrationChanged'],
  [14, 'teamUpdated'],
  [15, 'teamDeleted'],
  [16, 'teamMemberJoined'],
  [17, 'teamMemberRemoved'],
  [18, 'teamCaptainTransferred'],
  [19, 'teamBanned'],
  [20, 'teamUnbanned'],
  [21, 'submissionReceived'],
  [22, 'submissionEvaluated'],
  [23, 'scoringRecorded'],
  [24, 'firstBloodAwarded'],
  [25, 'secondBloodAwarded'],
  [26, 'thirdBloodAwarded'],
  [27, 'runtimeCreated'],
  [28, 'runtimeStateChanged'],
  [29, 'runtimeExtended'],
  [30, 'runtimeReset'],
  [31, 'runtimePortAllocated'],
  [32, 'announcementPublished'],
  [33, 'questionOpened'],
  [34, 'questionReplied'],
  [35, 'questionStatusChanged'],
  [36, 'questionPublished'],
  [37, 'protectedSubmissionFlagAccessed'],
  [38, 'cheatIncidentDetected'],
  [39, 'cheatIncidentConfirmed'],
  [40, 'cheatIncidentDismissed'],
  [41, 'cheatIncidentSuperseded'],
  [42, 'cheatIncidentCorrected'],
] as const satisfies ReadonlyArray<readonly [CompetitionEventKind, string]>

const competitionEventKindKeys = new Map<CompetitionEventKind, string>(
  competitionEventKindEntries,
)

export function competitionEventKindKey(kind?: CompetitionEventKind | null) {
  return kind === undefined || kind === null
    ? null
    : competitionEventKindKeys.get(kind) ?? null
}
