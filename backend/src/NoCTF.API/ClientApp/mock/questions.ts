import { type Data } from './schema'

/** Match the question endpoint's per-viewer affordances using only local fictional state. */
export function questionView(question: Data, user: Data | undefined, team: Data | undefined): Data {
  const handler = ['Administrator', 'Organizer'].includes(user?.role)
  const asker = user && (question.askedByUserId === user.userId || question.teamId === team?.id)
  const access = handler ? 'Handler' : asker ? 'Asker' : 'Observer'
  let consecutive = 0
  for (const entry of question.entries ?? []) {
    if (entry.kind !== 'Message') continue
    consecutive = entry.actorRole === 'Asker' || entry.actorRole === 'Participant' ? consecutive + 1 : 0
  }
  const maximum = question.maxParticipantMessagesBeforeHandlerReply ?? 5
  const remaining = Math.max(0, maximum - consecutive)
  const active = ['Pending', 'Replied'].includes(question.status)
  return { ...question, access, participantMessagesRemaining: remaining, maxParticipantMessagesBeforeHandlerReply: maximum,
    canReply: handler ? active : Boolean(asker && question.status !== 'Closed' && remaining > 0),
    canResolve: handler ? active : Boolean(asker && question.status === 'Replied'),
    canClose: handler && question.status !== 'Closed' }
}

export function questionActorRole(user: Data) {
  return user.role === 'Administrator' ? 'PlatformAdministrator' : user.role === 'Organizer' ? 'CompetitionManager' : 'Asker'
}
