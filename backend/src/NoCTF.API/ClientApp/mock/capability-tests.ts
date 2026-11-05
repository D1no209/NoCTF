import { now, type Data } from './schema'
import type { MockState } from './data/fixtures'

export function createMockCapabilityTests(state: MockState, json: (value: unknown, status?: number, headers?: HeadersInit) => Response) {
  const executions = new Map<string, Data>()
  const receipts = new Map<string, { fingerprint: string; accepted: Data }>()
  const fail = (status: number, code: string) => json({ code, detail: `Demo capability test: ${code}`,
    messageKey: `administration.capabilityTests.error.${code}`, messageArguments: {} }, status)
  return {
    async handle(request: Request, route: string, params: Data, body: Data, user?: Data): Promise<Response | undefined> {
      const root = '/admin/challenges/{challengeId}'
      if (!route.startsWith(root) || !(/\/(?:break|fix|check|control|questionnaire|fix-and-check)-tests$/.test(route)
        || route === `${root}/capability-tests/{executionId}`)) return
      const template = state.templates.find(value => value.id === params.challengeId && !value.deletedAt)
      if (!template || !user || user.role !== 'Administrator' && template.ownerId !== user.userId
        && !template.managerIds?.includes(user.userId)) return fail(404, 'NotFound')
      if (request.method === 'GET') {
        const execution = executions.get(params.executionId)
        if (!execution || execution.challengeId !== template.id) return fail(404, 'NotFound')
        const { challengeId: _, ...response } = execution
        return json(response)
      }
      const operation = route.slice(route.lastIndexOf('/') + 1)
      const kind = { 'break-tests': 'Break', 'fix-tests': 'Fix', 'check-tests': 'Check',
        'control-tests': 'Control', 'questionnaire-tests': 'Questionnaire', 'fix-and-check-tests': 'FixAndCheck' }[operation]
      const capabilities: Data[] = template.definition.capabilities
      const required = kind === 'FixAndCheck' ? ['Fix', 'Check'] : [kind]
      if (required.some(value => !capabilities.some(capability => capability.kind === value))) return fail(400, 'CapabilityMissing')
      let file: File | null = null
      if (kind === 'Fix' || kind === 'FixAndCheck') {
        const form = await request.formData()
        body = { requestId: form.get('requestId') }
        const upload = form.get('file')
        if (!(upload instanceof File) || upload.size === 0) return fail(400, 'ArchiveInvalid')
        file = upload
      }
      const runtime = body.runtimeInstanceId ? state.runtimes.find(value => value.id === body.runtimeInstanceId
        && value.challengeId === template.id && value.purpose === 'TemplateTest' && value.state === 'Running') : undefined
      if (body.runtimeInstanceId && !runtime) return fail(409, 'RuntimeNotReady')
      if (kind === 'Check' || kind === 'Control') {
        if (!runtime) return fail(409, 'RuntimeNotReady')
      }
      const executionId = crypto.randomUUID()
      if (kind === 'Break') return json({ executionId, match: body.flag === 'flag{mock_success}' ? 'Matched' : 'NotMatched',
        matchedFlagIds: body.flag === 'flag{mock_success}' ? [template.id] : [], observedAt: now() })
      if (kind === 'Control') return json({ executionId, result: 'Uncontrolled', controllerIdentifier: null,
        executionState: 'Completed', executionFailure: null, observedAt: now() })
      if (kind === 'Questionnaire') {
        const revision = capabilities.find(value => value.kind === kind)!.questionnaire
        const answers: Data[] = body.answers ?? []
        if (new Set(answers.map(answer => answer.questionId)).size !== answers.length
          || answers.some(answer => !revision.questions.some((question: Data) => question.id === answer.questionId
            && new Set(answer.selectedOptionIds).size === answer.selectedOptionIds.length
            && answer.selectedOptionIds.every((id: string) => question.options.some((option: Data) => option.id === id)))))
          return fail(400, 'InvalidInput')
        return json({ executionId, revisionId: revision.revisionId, observedAt: now(), questions: revision.questions.map((question: Data) => {
          const selected: string[] = answers.find(answer => answer.questionId === question.id)?.selectedOptionIds ?? []
          const correct: string[] = question.options.filter((option: Data) => option.isCorrect).map((option: Data) => option.id)
          return { questionId: question.id, result: !selected.length ? 'Unanswered'
            : selected.length === correct.length && selected.every(id => correct.includes(id)) ? 'Correct' : 'Wrong' }
        }) })
      }
      if (!body.requestId) return fail(400, 'InvalidInput')
      const digest = file ? Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', await file.arrayBuffer()))) : null
      const fingerprint = JSON.stringify({ kind, runtimeInstanceId: body.runtimeInstanceId, name: file?.name, digest })
      const key = `${user.userId}:${template.id}:${body.requestId}`
      const previous = receipts.get(key)
      if (previous) return previous.fingerprint === fingerprint
        ? json(previous.accepted, 202, { Location: previous.accepted.statusUrl }) : fail(409, 'RequestConflict')
      const runtimeInstanceId = runtime?.id ?? crypto.randomUUID()
      const accepted = { executionId, runtimeInstanceId, statusUrl: `/api/v1/admin/challenges/${template.id}/capability-tests/${executionId}` }
      const check = capabilities.find(value => value.kind === 'Check')?.check
      executions.set(executionId, { challengeId: template.id, executionId, runtimeInstanceId, kind, state: 'Completed',
        acceptedAt: now(), deadline: new Date(Date.now() + 300_000).toISOString(), completedAt: now(), failure: null, failureStage: null,
        fixResult: kind === 'Check' ? null : 'Applied', fixExitCode: kind === 'Check' ? null : 0, fixExecutionUncertain: false,
        checkCompleted: kind !== 'Fix', probes: kind === 'Fix' ? [] : (check?.requirements ?? []).map((value: string) => ({
          kind: value, flagId: null, result: 'Passed', reason: 'None',
        })) })
      receipts.set(key, { fingerprint, accepted })
      return json(accepted, 202, { Location: accepted.statusUrl })
    },
  }
}
