import { now, type Data } from './schema'

/** Stateful, local-only fixtures for exercising the real SDK and writeup views. */
export function createMockChallengeWriteUps(context: {
  state: { competitions: Data[]; challenges: Data[]; teams: Data[]; users: Data[] }
  json: (value: unknown, status?: number, headers?: HeadersInit) => Response
  pdf: (name: string) => Blob
  prepareDownload: (user: Data, path: string) => Response
}) {
  const { state, json } = context
  const roots: Data[] = [], documents = new Map<string, { markdown: string | null; pdf: Blob | null; fileName: string | null }>()
  const receipts = new Map<string, { deductionPercent: number; unlockedAt: string }>()
  const policies = new Map<string, Data>(), overrides = new Map<string, number>()
  for (const competition of state.competitions) {
    Object.assign(competition, { singleWriteUpsEnabled: true, singleWriteUpDeductionPercent: 20, singleWriteUpDeadlineHours: 24,
      singleWriteUpDeadlineAt: new Date(Date.parse(competition.endTime) + 24 * 3600000).toISOString() })
    policies.set(competition.id, { stamp: crypto.randomUUID() })
    for (const challenge of state.challenges.filter(x => x.competitionId === competition.id).slice(0, 2)) {
      const version = { id: crypto.randomUUID(), number: 1, format: 'Markdown', state: 'Approved', actorUserId: state.users[0]!.userId,
        actorDisplayName: state.users[0]!.userName, updatedAt: now(), submittedAt: now(), publishedAt: now(), reviewReason: null, concurrencyStamp: crypto.randomUUID() }
      roots.push({ id: crypto.randomUUID(), competitionId: competition.id, competitionChallengeId: challenge.id,
        challengeTitle: challenge.title, source: 'Official', teamId: null, authorName: 'Official', concurrencyStamp: crypto.randomUUID(),
        publishedVersionId: version.id, published: version, submitted: version, draft: null, updatedAt: now(), versions: [version], viewedTeamCount: 0 })
      documents.set(version.id, { markdown: `## ${challenge.title}\n\n1. Review the challenge resources.\n2. Identify the vulnerable operation.\n\n\`\`\`python\nprint("flag{mock_success}")\n\`\`\`\n\nThis solution exists only in the local demo.`, pdf: null, fileName: null })
      const team = state.teams.find(t => t.competitionId === competition.id && !t.memberIds.includes(state.users[2]!.userId))
      if (team) {
        const revision = { ...version, id: crypto.randomUUID(), format: 'Pdf' }
        roots.push({ ...roots.at(-1), id: crypto.randomUUID(), source: 'Team', teamId: team.id, authorName: team.name,
          publishedVersionId: revision.id, published: revision, submitted: revision, versions: [revision] })
        documents.set(revision.id, { markdown: null, pdf: context.pdf(team.name), fileName: 'solution.pdf' })
      }
    }
  }
  const failure = (code: string, status = 409) => json({ code, messageKey: `challengeWriteUp.error.${code}` }, status)
  const view = (root: Data, privateAccess: boolean) => {
    const { competitionId, ...value } = root
    return privateAccess ? value : { ...value, draft: null, submitted: null, versions: root.published ? [root.published] : [] }
  }
  function settings(competition: Data, challengeId?: string) {
    const override = challengeId ? overrides.get(challengeId) : undefined
    return { enabled: competition.singleWriteUpsEnabled, deductionPercent: override ?? competition.singleWriteUpDeductionPercent,
      challengeDeductionPercent: override ?? null, deadlineHours: competition.singleWriteUpDeadlineHours,
      deadlineAt: competition.singleWriteUpDeadlineAt, policyStamp: policies.get(competition.id)!.stamp }
  }
  async function handle(request: Request, route: string, p: Data, body: Data, user?: Data): Promise<Response | null> {
    if (!route.includes('writeup-settings') && !route.includes('challenge-writeups') && !route.includes('/challenges/{competitionChallengeId}/writeups')) return null
    if (!user) return failure('Forbidden', 401)
    const competition = state.competitions.find(x => x.id === p.competitionId)
    if (!competition) return failure('NotFound', 404)
    const staff = user.role === 'Administrator' || user.role === 'Organizer'
    const team = state.teams.find(x => x.competitionId === competition.id && x.memberIds.includes(user.userId))
    const url = new URL(request.url), challengeId = p.competitionChallengeId ?? body.competitionChallengeId ?? url.searchParams.get('competitionChallengeId')
    const challenge = state.challenges.find(x => x.competitionId === competition.id && x.id === challengeId)
    const policy = settings(competition, challengeId)
    if (route.includes('writeup-settings')) {
      if (request.method === 'PATCH') {
        if (!staff) return failure('Forbidden', 403)
        if (body.expectedStamp !== policies.get(competition.id)!.stamp) return failure('Conflict')
        if (challengeId) { if (body.deductionPercent == null) overrides.delete(challengeId); else overrides.set(challengeId, body.deductionPercent) }
        else {
          if (body.enabled != null) competition.singleWriteUpsEnabled = body.enabled
          if (body.deductionPercent != null) competition.singleWriteUpDeductionPercent = body.deductionPercent
          if (body.deadlineHours != null) competition.singleWriteUpDeadlineHours = body.deadlineHours
          competition.singleWriteUpDeadlineAt = new Date(Date.parse(competition.endTime) + competition.singleWriteUpDeadlineHours * 3600000).toISOString()
        }
        policies.get(competition.id)!.stamp = crypto.randomUUID()
      }
      return json({ settings: settings(competition, challengeId), concurrencyStamp: policies.get(competition.id)!.stamp })
    }
    if (route === '/admin/competitions/{competitionId}/challenge-writeups') {
      if (!staff) return failure('Forbidden', 403)
      const filter = url.searchParams.get('filter'), source = url.searchParams.get('source'), search = url.searchParams.get('search')?.toLowerCase()
      const items = roots.filter(x => x.competitionId === competition.id && (!source || x.source === source)
        && (!search || `${x.challengeTitle} ${x.authorName}`.toLowerCase().includes(search))
        && (!filter || filter === 'All' || filter === 'Published' && x.published || filter === 'Draft' && x.draft
          || filter === 'Submitted' && x.submitted?.state === 'Submitted' || filter === 'Rejected' && x.submitted?.state === 'Rejected'))
      const offset = Number(url.searchParams.get('offset') ?? 0), limit = Number(url.searchParams.get('limit') ?? 20)
      return json({ items: items.slice(offset, offset + limit).map(x => view(x, true)), totalCount: items.length, canManage: true, canJudge: true })
    }
    if (!competition.singleWriteUpsEnabled && !(staff && url.searchParams.get('staff') === 'true')) return failure('Disabled', 403)
    if (!team || team.isBanned || team.registrationStatus !== 'Approved') { if (!staff) return failure('Forbidden', 403) }
    if (route.endsWith('/teams/me/challenge-writeups')) return json(roots.filter(x => x.competitionId === competition.id && x.teamId === team?.id).map(x => view(x, true)))
    if (!challenge || challenge.deletedAt || !challenge.isPublished && !staff) return failure('NotFound', 404)
    const topicRoots = roots.filter(x => x.competitionChallengeId === challengeId)
    const receiptKey = `${team?.id}:${challengeId}`, receipt = receipts.get(receiptKey)
    const free = competition.status === 'Finished', deadline = Date.parse(policy.deadlineAt) >= Date.now()
    const useStaff = staff && (url.searchParams.get('staff') === 'true' || body.staff === true)
    if (route.endsWith('/writeups')) return json({ items: topicRoots.filter(x => useStaff || x.published || x.teamId === team?.id)
      .map(x => view(x, useStaff || x.teamId === team?.id)), access: { canManage: staff, canJudge: staff, canSubmit: deadline,
        isFree: free, isUnlocked: !!receipt, deductionPercent: receipt?.deductionPercent ?? policy.deductionPercent,
        unlockedAt: receipt?.unlockedAt ?? null, canUnlock: competition.status === 'Running', teamId: team?.id, settings: policy, canShowCurrentScore: true } })
    if (route.endsWith('/draft') || route.endsWith('/draft/pdf') || route.endsWith('/submit')) {
      let input = body, upload: File | null = null
      if (route.endsWith('/pdf')) {
        const form = await request.formData(); input = Object.fromEntries(form.entries()); upload = form.get('file') as File
        input.official = input.official === 'true'
      }
      const official = input.official === true
      if (official && !staff || !official && !deadline) return failure('Forbidden', 403)
      let root = topicRoots.find(x => official ? x.source === 'Official' : x.teamId === team?.id)
      if (root && root.concurrencyStamp !== input.expectedStamp) return failure('Conflict')
      if (!root) {
        root = { id: crypto.randomUUID(), competitionId: competition.id, competitionChallengeId: challengeId, challengeTitle: challenge.title,
          source: official ? 'Official' : 'Team', teamId: official ? null : team?.id, authorName: official ? 'Official' : team?.name,
          concurrencyStamp: crypto.randomUUID(), publishedVersionId: null, published: null, submitted: null, draft: null, versions: [], viewedTeamCount: 0 }
        roots.push(root)
      }
      if (route.endsWith('/submit')) {
        if (!root.draft) return failure('InvalidContent', 400)
        Object.assign(root.draft, { state: 'Submitted', submittedAt: now() }); root.submitted = root.draft; root.draft = null
      }
      else {
        if (!upload && !input.markdown?.trim()) return failure('InvalidContent', 400)
        const draft = root.draft ?? { id: crypto.randomUUID(), number: root.versions.length + 1, state: 'Draft',
          actorUserId: user.userId, actorDisplayName: user.userName, submittedAt: null, reviewReason: null }
        Object.assign(draft, { format: upload ? 'Pdf' : 'Markdown', updatedAt: now(), concurrencyStamp: crypto.randomUUID() })
        if (!root.draft) root.versions.unshift(draft)
        root.draft = draft; documents.set(draft.id, { markdown: upload ? null : input.markdown, pdf: upload, fileName: upload?.name ?? null })
      }
      root.updatedAt = now(); root.concurrencyStamp = crypto.randomUUID()
      return json(view(root, true))
    }
    const root = topicRoots.find(x => p.writeUpId ? x.id === p.writeUpId : x.versions.some((v: Data) => v.id === p.versionId))
    if (!root) return failure('NotFound', 404)
    if (route.endsWith('/review')) {
      if (!staff) return failure('Forbidden', 403)
      if (root.concurrencyStamp !== body.expectedStamp) return failure('Conflict')
      const version = root.versions.find((x: Data) => x.id === body.versionId)
      if (!version) return failure('NotFound', 404)
      if (body.action === 'Withdraw') { root.published = null; root.publishedVersionId = null }
      else if (body.action === 'Reject') { version.state = 'Rejected'; version.reviewReason = body.reason }
      else if (body.action === 'Publish') { version.state = 'Approved'; version.publishedAt = now(); root.published = version; root.publishedVersionId = version.id }
      else return failure('InvalidContent', 400)
      root.concurrencyStamp = crypto.randomUUID(); return json(view(root, true))
    }
    const version = root.versions.find((x: Data) => x.id === p.versionId), doc = documents.get(p.versionId)
    const own = root.teamId === team?.id, published = root.publishedVersionId === p.versionId
    if (!version || !doc || !useStaff && !own && !published) return failure('NotPublished', 404)
    if (route.endsWith('/quote')) return json({ versionId: version.id, policyStamp: policy.policyStamp, isFree: free || own,
      isUnlocked: !!receipt, canUnlock: competition.status === 'Running', deductionPercent: receipt?.deductionPercent ?? policy.deductionPercent,
      grossPoints: 500, estimatedDeductionPoints: Math.ceil(500 * (receipt?.deductionPercent ?? policy.deductionPercent) / 100) })
    if (route.endsWith('/unlock')) {
      if (!receipt && !own && !free) {
        if (competition.status !== 'Running') return failure('CompetitionNotRunning')
        if (body.policyStamp !== policy.policyStamp) return failure('ConfirmationChanged')
        receipts.set(receiptKey, { deductionPercent: policy.deductionPercent, unlockedAt: now() }); root.viewedTeamCount++
      }
      return json({ created: !receipt && !own && !free, deductionPercent: receipts.get(receiptKey)?.deductionPercent ?? policy.deductionPercent })
    }
    if (!useStaff && !own && !free && !receipt) return failure('Forbidden', 403)
    if (route.endsWith('/browser-access')) {
      const path = `/api/v1/competitions/${competition.id}/challenges/${challengeId}/writeups/versions/${version.id}/file`
      const grant = context.prepareDownload(user, path)
      return json({ previewUrl: `${path}?staff=${body.staff === true}`, downloadUrl: `${path}?staff=${body.staff === true}&download=true` }, 200, { 'Set-Cookie': grant.headers.get('Set-Cookie') ?? '' })
    }
    if (route.endsWith('/file')) return doc.pdf ? new Response(doc.pdf, { headers: { 'Content-Type': 'application/pdf', 'Cache-Control': 'private,no-store',
      'Content-Disposition': `${url.searchParams.get('download') === 'true' ? 'attachment' : 'inline'}; filename="solution.pdf"` } }) : failure('ContentUnavailable', 404)
    return json({ versionId: version.id, format: version.format, markdown: doc.markdown, fileName: doc.fileName })
  }
  return { handle, roots, receipts }
}
