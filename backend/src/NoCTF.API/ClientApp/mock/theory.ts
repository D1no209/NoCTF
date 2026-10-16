import { date, id, model, now, type Data } from './schema'

const staffRoles = ['Administrator', 'Organizer']
const maximumImageBytes = 5 * 1024 * 1024

type SeedOption = { text: string; correct?: boolean; truth?: 0 | 1 }
type SeedQuestion = {
  title: string
  body: string
  explanation: string
  kind: 0 | 1 | 2
  difficulty: 0 | 1 | 2
  state: 0 | 1 | 2
  knowledge: string
  tags: string[]
  options: SeedOption[]
}

const seedQuestions: SeedQuestion[] = [
  { title: 'CIA 三元组中的完整性', body: '以下哪一项最能描述 CIA 三元组中的「完整性」？', explanation: '完整性关注数据是否被未授权修改；机密性关注读取范围，可用性关注服务连续性。', kind: 0, difficulty: 0, state: 1, knowledge: '网络安全基础', tags: ['基础'], options: [
    { text: '保证数据未被未授权篡改', correct: true }, { text: '保证数据只被授权者读取' }, { text: '保证服务持续可用' }, { text: '保证日志不会被删除' }] },
  { title: 'TLS 握手中的服务端身份', body: 'TLS 握手阶段，客户端验证服务端身份主要依赖什么？', explanation: '服务端证书链由受信任的 CA 签发，客户端据此确认对端身份。', kind: 0, difficulty: 1, state: 1, knowledge: '网络安全基础', tags: ['基础', 'TLS'], options: [
    { text: '服务端证书链及其信任锚', correct: true }, { text: '会话密钥长度' }, { text: 'SNI 扩展中的主机名' }, { text: '压缩算法协商结果' }] },
  { title: '对称加密与数字签名', body: '对称加密算法可以直接用于数字签名。', explanation: '数字签名需要只有签名者持有的私钥；对称密钥双方共享，无法提供不可否认性。', kind: 2, difficulty: 0, state: 1, knowledge: '网络安全基础', tags: ['基础'], options: [
    { text: 'True', truth: 0 }, { text: 'False', truth: 1, correct: true }] },
  { title: '多因素认证的构成', body: '以下哪些属于多因素认证中的「因素」？', explanation: '口令属于所知，硬件令牌属于所持，指纹属于所是；用户名只是标识。', kind: 1, difficulty: 1, state: 1, knowledge: '网络安全基础', tags: ['基础'], options: [
    { text: '你记得的口令', correct: true }, { text: '你持有的硬件令牌', correct: true }, { text: '你的指纹', correct: true }, { text: '你的用户名' }] },
  { title: 'AES 的算法类别', body: 'AES 属于哪一类密码算法？', explanation: 'AES 是分组对称加密算法，分组长度 128 位，密钥长度 128/192/256 位。', kind: 0, difficulty: 1, state: 1, knowledge: '密码学', tags: ['对称加密'], options: [
    { text: '分组对称加密算法', correct: true }, { text: '流式密码算法' }, { text: '非对称加密算法' }, { text: '哈希函数' }] },
  { title: 'HMAC 的安全目标', body: 'HMAC 的主要安全目标是什么？', explanation: 'HMAC 使用共享密钥为消息提供完整性与来源认证。', kind: 0, difficulty: 2, state: 1, knowledge: '密码学', tags: ['公钥'], options: [
    { text: '消息完整性与来源认证', correct: true }, { text: '消息机密性' }, { text: '抗量子攻击' }, { text: '压缩传输体积' }] },
  { title: 'RSA 密钥的互换使用', body: '同一对 RSA 密钥用于加密与签名时可以互换使用。', explanation: '两种用途的填充方案与安全假设不同，生产实现必须区分。', kind: 2, difficulty: 1, state: 1, knowledge: '密码学', tags: ['公钥'], options: [
    { text: 'True', truth: 0 }, { text: 'False', truth: 1, correct: true }] },
  { title: '抵抗离线口令破解', body: '以下哪些实践可以降低口令被离线破解的风险？', explanation: '内存硬的 KDF 与独立随机盐是口令存储的基线要求。', kind: 1, difficulty: 2, state: 1, knowledge: '密码学', tags: ['对称加密'], options: [
    { text: '使用 Argon2 等内存硬 KDF', correct: true }, { text: '为每个口令生成独立的随机盐', correct: true }, { text: '使用单轮 MD5 计算摘要' }, { text: '把口令摘要写入公开日志' }] },
  { title: '存储型 XSS 的防御', body: '以下哪种做法能最有效地防御存储型 XSS？', explanation: '输出编码必须与目标上下文匹配；仅在前端过滤无法覆盖服务端渲染。', kind: 0, difficulty: 0, state: 1, knowledge: 'Web 安全', tags: ['XSS', '基础'], options: [
    { text: '在输出到 HTML 上下文时进行上下文相关的转义', correct: true }, { text: '只在前端过滤 <script> 字符串' }, { text: '把所有接口改为 GET 请求' }, { text: '关闭浏览器 Cookie' }] },
  { title: '参数化查询的防护目标', body: '参数化查询主要用于防御哪一类漏洞？', explanation: '参数与语句分离，避免数据被当作 SQL 语法解析。', kind: 0, difficulty: 1, state: 1, knowledge: 'Web 安全', tags: ['SQL'], options: [
    { text: 'SQL 注入', correct: true }, { text: '跨站脚本' }, { text: '跨站请求伪造' }, { text: '服务端请求伪造' }] },
  { title: '同源策略的组成', body: '以下哪些属于同源策略（SOP）中「源」的组成部分？', explanation: '源由协议、主机与端口组成，路径不参与同源判定。', kind: 1, difficulty: 2, state: 1, knowledge: 'Web 安全', tags: ['HTTP'], options: [
    { text: '协议', correct: true }, { text: '主机', correct: true }, { text: '端口', correct: true }, { text: '路径' }] },
  { title: 'HttpOnly 的作用', body: 'HttpOnly 标记可以阻止 JavaScript 读取该 Cookie。', explanation: 'HttpOnly 使 document.cookie 无法访问该 Cookie，降低 XSS 窃取会话的风险。', kind: 2, difficulty: 0, state: 1, knowledge: 'Web 安全', tags: ['HTTP', '基础'], options: [
    { text: 'True', truth: 0, correct: true }, { text: 'False', truth: 1 }] },
  { title: 'ELF 程序入口地址', body: '在 ELF 可执行文件中，程序入口地址由哪个结构给出？', explanation: 'ELF 头中的 e_entry 字段给出入口虚拟地址。', kind: 0, difficulty: 1, state: 1, knowledge: '逆向工程', tags: ['汇编'], options: [
    { text: 'ELF 头中的 e_entry', correct: true }, { text: '.text 段的起始偏移' }, { text: '动态符号表' }, { text: '全局偏移表 GOT' }] },
  { title: 'ROP 的核心思想', body: '栈溢出利用中 ROP 的核心思想是什么？', explanation: 'ROP 复用已加载代码中的指令片段并以 ret 串联，绕过不可执行栈限制。', kind: 0, difficulty: 2, state: 0, knowledge: '逆向工程', tags: ['内存'], options: [
    { text: '复用已有代码片段以控制执行流', correct: true }, { text: '直接修改内核模块' }, { text: '加密注入的 shellcode' }, { text: '提升进程调度优先级' }] },
  { title: 'UPX 加壳程序的执行', body: 'UPX 加壳后的程序无需解压即可直接执行原始代码。', explanation: '加壳程序运行时先由壳代码在内存中解压原始映像，再跳转执行。', kind: 2, difficulty: 0, state: 1, knowledge: '逆向工程', tags: ['汇编', '基础'], options: [
    { text: 'True', truth: 0 }, { text: 'False', truth: 1, correct: true }] },
  { title: '符号表可以还原的信息', body: '以下哪些信息通常可以从符号表中获得？', explanation: '符号表描述函数与全局变量的名称和地址，不含运行时堆地址或口令。', kind: 1, difficulty: 1, state: 1, knowledge: '逆向工程', tags: ['汇编'], options: [
    { text: '函数名称', correct: true }, { text: '全局变量名称', correct: true }, { text: '运行时堆地址' }, { text: '用户口令' }] },
  { title: '磁盘取证的写保护', body: '磁盘取证中启用写保护的首要目的是什么？', explanation: '写保护确保检验过程不会修改原始证据，保持证据可采性。', kind: 0, difficulty: 0, state: 1, knowledge: '数字取证', tags: ['基础'], options: [
    { text: '避免检验过程改动原始证据', correct: true }, { text: '加快镜像写入速度' }, { text: '压缩镜像体积' }, { text: '兼容更多文件系统' }] },
  { title: '用户活动痕迹的分析对象', body: '分析 Windows 用户最近打开过哪些文件时，最有价值的取证对象是？', explanation: 'Jump List、Shellbag 等 Artifact 直接记录用户交互与路径访问。', kind: 0, difficulty: 1, state: 1, knowledge: '数字取证', tags: ['日志'], options: [
    { text: 'Jump List 与 Shellbag 等用户活动痕迹', correct: true }, { text: '页面文件的大小' }, { text: '显示器型号' }, { text: '键盘布局设置' }] },
  { title: '内存证据的可采性', body: '以下哪些做法有助于维持内存取证证据的可采性？', explanation: '记录采集元数据并计算镜像摘要是取证基线要求。', kind: 1, difficulty: 2, state: 0, knowledge: '数字取证', tags: ['日志', '内存'], options: [
    { text: '记录采集时间、工具与版本', correct: true }, { text: '计算并保存镜像摘要', correct: true }, { text: '直接在被取证主机上分析内存内容' }, { text: '采集前先重启主机' }] },
  { title: '镜像摘要的一致性', body: '磁盘镜像的 SHA-256 在分析前后应当保持一致。', explanation: '摘要一致说明镜像未被修改，是证据完整性的直接证明。', kind: 2, difficulty: 0, state: 1, knowledge: '数字取证', tags: ['基础'], options: [
    { text: 'True', truth: 0, correct: true }, { text: 'False', truth: 1 }] },
  { title: '对象存储的高危配置', body: '云上对象存储最常见的高危配置是？', explanation: '允许匿名或公开列出与读取对象会直接泄露业务数据。', kind: 0, difficulty: 0, state: 1, knowledge: '云安全', tags: ['容器'], options: [
    { text: '允许匿名或公开列出与读取对象', correct: true }, { text: '启用服务端加密' }, { text: '启用版本控制' }, { text: '启用访问日志' }] },
  { title: 'IAM 权限边界', body: 'IAM 权限边界（permissions boundary）的作用是什么？', explanation: '权限边界定义身份可获得的权限上限，即使策略本身允许更多动作。', kind: 0, difficulty: 1, state: 1, knowledge: '云安全', tags: ['IAM'], options: [
    { text: '限制该身份可获得的权限上限', correct: true }, { text: '加密访问密钥' }, { text: '记录 API 调用日志' }, { text: '保护根账号登录' }] },
  { title: '最小权限实践', body: '以下哪些属于最小权限实践？', explanation: '按任务授予细粒度动作并定期回收闲置权限；共用管理员密钥与硬编码密钥都违背最小权限。', kind: 1, difficulty: 2, state: 2, knowledge: '云安全', tags: ['IAM'], options: [
    { text: '按任务授予细粒度动作与资源范围', correct: true }, { text: '定期回收长期未使用的权限', correct: true }, { text: '为所有服务共用一把管理员密钥' }, { text: '把密钥硬编码进容器镜像' }] },
  { title: '容器与宿主内核', body: '容器中运行的进程仍然共享宿主机内核。', explanation: '容器依赖命名空间与 cgroups 隔离，内核仍是共享的，因此内核漏洞影响范围更大。', kind: 2, difficulty: 0, state: 1, knowledge: '云安全', tags: ['容器', '基础'], options: [
    { text: 'True', truth: 0, correct: true }, { text: 'False', truth: 1 }] },
]

const fictionalParticipants = [
  [id(47, 1), '林澈'], [id(47, 2), '苏晚'], [id(47, 3), '陆离'],
] as const

/** Deterministic local-only fixtures for every theory route the ClientApp calls. */
export function createMockTheory(context: {
  state: { users: Data[]; teams: Data[]; competitions: Data[]; questions: Data[] }
  json: (value: unknown, status?: number, headers?: HeadersInit) => Response
  shape: (schema: Data | undefined, value: any) => any
  responseSchema: (operation: Data, status?: string) => Data | undefined
}) {
  const { state, json, shape, responseSchema } = context
  const questions = new Map<string, Data>()
  const questionImages = new Map<string, { blob: Blob; questionId: string }>()
  const activities = new Map<string, Data>()
  const papers = new Map<string, Data>()
  const previews = new Map<string, Data>()
  const issueReceipts = new Map<string, string>()
  const answerReceipts = new Map<string, { paperId: string; itemId: string }>()
  const changes = new Set<(activityId: string) => void>()
  let paperCounter = 0

  const isStaff = (user?: Data) => staffRoles.includes(user?.role)
  const failure = (status: number, detail: string, code?: string) =>
    json({ status, title: 'Mock API', detail, ...(code ? { code } : {}) }, status)
  const respond = (operation: Data, value: any, status = 200) => {
    if (status === 204) return new Response(null, { status, headers: { 'X-NoCTF-Mock': 'true' } })
    const schema = responseSchema(operation, String(status)) ?? responseSchema(operation)
    return json(shape(schema, value), status)
  }
  const changed = (activityId: string) => { for (const notify of changes) notify(activityId) }
  const userName = (userId: string) => state.users.find(user => user.userId === userId)?.userName
    ?? fictionalParticipants.find(([candidate]) => candidate === userId)?.[1] ?? '演示选手 / Mock Candidate'
  const sameAnswer = (left: string[], right: string[]) =>
    left.length === right.length && new Set(left).size === left.length && left.every(value => right.includes(value))

  // ---------------------------------------------------------------- question bank
  seedQuestions.forEach((seed, index) => {
    const questionId = id(40, index + 1)
    questions.set(questionId, {
      id: questionId, ownerId: index % 7 === 6 ? id(1, 2) : id(1), sourceId: questionId,
      version: 1 + (index % 4), revisionId: id(41, index + 1),
      title: seed.title, body: seed.body, explanation: seed.explanation,
      kind: seed.kind, difficulty: seed.difficulty, state: seed.state,
      updatedAt: date(-(index + 1)), firstExposedAt: null, paperReferences: 0, lastIssuedAt: null,
      classifications: [{ kind: 0, name: seed.knowledge }, ...seed.tags.map(name => ({ kind: 1, name }))],
      options: seed.options.map((option, optionIndex) => ({ id: id(42, (index + 1) * 10 + optionIndex + 1),
        order: optionIndex, content: option.text, isCorrect: option.correct === true, truthValue: option.truth ?? null })),
      fileIds: [] as string[], grants: new Map<string, number>(),
    })
  })
  // Two explicit collaborator grants keep the use-only and edit permission branches demonstrable.
  questions.get(id(40, 5))!.grants.set(id(1, 3), 0)
  questions.get(id(40, 10))!.grants.set(id(1, 3), 1)

  function canReadAnswers(question: Data, user: Data) {
    const grant = question.grants.get(user.userId)
    return isStaff(user) || question.ownerId === user.userId || (grant ?? -1) >= 1
  }
  function canManageQuestion(question: Data, user: Data) {
    const grant = question.grants.get(user.userId)
    return isStaff(user) || question.ownerId === user.userId || grant === 2
  }
  const optionViews = (question: Data) => (question.options as Data[]).map((option, index) => ({
    id: option.id, order: index, content: option.content, truthValue: option.truthValue ?? null }))

  function questionSummary(question: Data, user: Data) {
    return { id: question.id, ownerId: question.ownerId, sourceId: question.sourceId, version: question.version,
      title: question.title, kind: question.kind, difficulty: question.difficulty, state: question.state,
      updatedAt: question.updatedAt, canReadAnswers: canReadAnswers(question, user), canManage: canManageQuestion(question, user),
      firstExposedAt: question.firstExposedAt ?? null, paperReferences: question.paperReferences ?? 0,
      classifications: question.classifications }
  }
  function questionContent(question: Data, user: Data) {
    return { question: questionSummary(question, user), revisionId: question.revisionId,
      content: { title: question.title, body: question.body, explanation: question.explanation ?? null,
        kind: question.kind, difficulty: question.difficulty, options: optionViews(question),
        classifications: question.classifications, fileIds: question.fileIds },
      grants: [...(question.grants as Map<string, number>).entries()].map(([userId, permission]) => ({
        userId, name: userName(userId), permission })) }
  }
  function validateContent(content: Data): string | null {
    const options: Data[] = Array.isArray(content.options) ? content.options : []
    if (typeof content.title !== 'string' || !content.title.trim()) return '题目名称必填 / Question title is required'
    if (typeof content.body !== 'string' || !content.body.trim()) return '题干必填 / Question body is required'
    if (options.length < 2 || options.length > 8) return '选项数量必须为 2-8 / Between 2 and 8 options are required'
    if (options.some(option => typeof option.content !== 'string' || !option.content.trim())) return '每个选项都需要内容 / Every option needs content'
    if (!options.some(option => option.isCorrect === true)) return '至少需要一个正确选项 / At least one correct option is required'
    if (Number(content.kind ?? 0) !== 1 && options.filter(option => option.isCorrect === true).length !== 1)
      return '单选题只能有一个正确选项 / Single choice allows exactly one correct option'
    if (![0, 1, 2].includes(Number(content.kind ?? 0))) return '题目类型无效 / Unknown question kind'
    if (![0, 1, 2].includes(Number(content.difficulty ?? 0))) return '难度无效 / Unknown difficulty'
    return null
  }
  function applyContent(question: Data, content: Data) {
    question.title = content.title
    question.body = content.body
    question.explanation = content.explanation ?? null
    question.kind = Number(content.kind ?? 0)
    question.difficulty = Number(content.difficulty ?? 0)
    question.options = (content.options as Data[]).map((option, index) => ({ id: crypto.randomUUID(), order: index,
      content: option.content, isCorrect: option.isCorrect === true, truthValue: option.truthValue ?? null }))
    question.classifications = (content.classifications ?? [])
      .map((item: Data) => ({ kind: Number(item.kind ?? 0), name: String(item.name ?? '') }))
      .filter((item: Data) => item.name)
    question.fileIds = (content.fileIds ?? []).map((fileId: unknown) => String(fileId))
  }

  // ---------------------------------------------------------------- activities
  function buildRules(rules: Data[]): Data[] {
    return rules.map((rule, index) => ({ id: id(48, index + 1), kind: Number(rule.kind ?? 0),
      difficulty: rule.difficulty == null ? null : Number(rule.difficulty), count: Number(rule.count ?? 1),
      points: Number(rule.points ?? 1),
      filters: (rule.filters ?? []).map((filter: Data) => ({ kind: Number(filter.kind ?? 0), name: String(filter.name ?? '') })) }))
  }
  const maximumPoints = (activity: Data) => activity.rules.reduce((total: number, rule: Data) => total + rule.count * rule.points, 0)
  const questionCount = (activity: Data) => activity.rules.reduce((total: number, rule: Data) => total + rule.count, 0)

  const seedActivities: Data[] = [
    { index: 1, name: '理论练习 · 网络安全基础', kind: 0, competitionId: null, state: 1, opensAt: date(-72), closesAt: date(240),
      durationSeconds: 1800, attemptLimit: null, scoringMode: 0, allowExposedQuestions: false, issuanceStopped: false, pausedMs: 0,
      rules: [{ kind: 0, count: 4, points: 10 }, { kind: 2, count: 2, points: 10 }] },
    { index: 2, name: '理论测验 · 计时考试', kind: 1, competitionId: null, state: 1, opensAt: date(-24), closesAt: date(96),
      durationSeconds: 2400, attemptLimit: 3, scoringMode: 1, allowExposedQuestions: false, issuanceStopped: false, pausedMs: 0,
      rules: [{ kind: 0, count: 5, points: 10 }, { kind: 1, count: 3, points: 20 }] },
    { index: 3, name: 'NoCTF 春季挑战赛 · 理论笔试', kind: 2, competitionId: id(2, 1), state: 1, opensAt: date(-12), closesAt: date(36),
      durationSeconds: 1800, attemptLimit: 1, scoringMode: 0, allowExposedQuestions: true, issuanceStopped: false, pausedMs: 0,
      rules: [{ kind: 0, count: 6, points: 10 }, { kind: 1, count: 2, points: 15 }] },
    { index: 4, name: '理论演练 · 草稿活动', kind: 0, competitionId: null, state: 0, opensAt: date(24), closesAt: date(72),
      durationSeconds: 1800, attemptLimit: null, scoringMode: 0, allowExposedQuestions: false, issuanceStopped: false, pausedMs: 0,
      rules: [{ kind: 0, count: 3, points: 10 }] },
    { index: 5, name: '夏季邀请赛 · 理论补考', kind: 2, competitionId: id(2, 6), state: 2, opensAt: date(-120), closesAt: date(-96),
      durationSeconds: 1800, attemptLimit: 1, scoringMode: 0, allowExposedQuestions: false, issuanceStopped: false, pausedMs: 0,
      rules: [{ kind: 0, count: 5, points: 10 }] },
  ]
  seedActivities.forEach(seed => {
    const activity: Data = { id: id(43, seed.index), competitionId: seed.competitionId, name: seed.name, kind: seed.kind,
      state: seed.state, opensAt: seed.opensAt, closesAt: seed.closesAt, durationSeconds: seed.durationSeconds,
      attemptLimit: seed.attemptLimit, scoringMode: seed.scoringMode, allowExposedQuestions: seed.allowExposedQuestions,
      issuanceStopped: seed.issuanceStopped, paused: false, pausedAt: null, pausedMs: seed.pausedMs,
      rules: buildRules(seed.rules), audit: [], corrections: [], createdBy: id(1) }
    activity.audit.push({ id: id(46, seed.index * 10 + 1), kind: 0, actorName: 'Mock Admin', occurredAt: seed.opensAt, reason: null })
    if (seed.allowExposedQuestions) activity.audit.push({ id: id(46, seed.index * 10 + 2), kind: 8, actorName: 'Mock Admin',
      occurredAt: new Date(Date.parse(seed.opensAt) + 1_800_000).toISOString(),
      reason: '允许重复抽题 / Reuse previously exposed questions' })
    if (seed.state === 2) activity.audit.push({ id: id(46, seed.index * 10 + 3), kind: 6, actorName: 'Mock Admin',
      occurredAt: seed.closesAt, reason: '活动结束 / Activity finished' })
    activities.set(activity.id, activity)
  })

  const activitySummary = (activity: Data) => ({ id: activity.id, competitionId: activity.competitionId, name: activity.name,
    kind: activity.kind, state: activity.state, opensAt: activity.opensAt, closesAt: activity.closesAt,
    durationSeconds: activity.durationSeconds, attemptLimit: activity.attemptLimit, scoringMode: activity.scoringMode,
    maximumPaperPoints: maximumPoints(activity), issuanceStopped: activity.issuanceStopped, questionCount: questionCount(activity) })

  function competitionEndOf(activity: Data): number | null {
    const competition = activity.competitionId ? state.competitions.find(row => row.id === activity.competitionId) : null
    return competition ? Date.parse(competition.endTime) : null
  }
  const formatDuration = (milliseconds: number) => {
    const seconds = Math.max(0, Math.floor(milliseconds / 1000))
    return [Math.floor(seconds / 3600), Math.floor(seconds / 60) % 60, seconds % 60]
      .map(value => String(value).padStart(2, '0')).join(':')
  }
  function activityDeadline(activity: Data) {
    const closes = Date.parse(activity.closesAt)
    const cap = competitionEndOf(activity)
    const at = cap == null ? closes : Math.min(closes, cap)
    return { at: new Date(at).toISOString(), extension: formatDuration(activity.pausedMs), paused: activity.paused === true,
      capped: cap != null && cap < closes }
  }
  function paperDeadline(activity: Data, paper: Data | null) {
    const base = activityDeadline(activity)
    if (!paper || paper.state !== 0) return base
    const limit = Date.parse(paper.openedAt) + activity.durationSeconds * 1000 + activity.pausedMs
    return { ...base, at: new Date(Math.min(limit, Date.parse(base.at))).toISOString() }
  }
  function poolFor(rule: Data): Data[] {
    return [...questions.values()].filter(question => question.state === 1 && question.kind === rule.kind
      && (rule.difficulty == null || question.difficulty === rule.difficulty)
      && rule.filters.every((filter: Data) => question.classifications.some((item: Data) => item.kind === filter.kind && item.name === filter.name)))
      .sort((left, right) => String(left.id).localeCompare(String(right.id)))
  }
  function activityCheck(activity: Data) {
    const rules = activity.rules.map((rule: Data, order: number) => ({ order, rule, available: poolFor(rule).length }))
    return { feasible: rules.every((entry: Data) => entry.available >= entry.rule.count),
      rules: rules.map((entry: Data) => ({ order: entry.order, required: entry.rule.count, available: entry.available })),
      shortages: rules.filter((entry: Data) => entry.available < entry.rule.count)
        .map((entry: Data) => ({ ruleId: entry.rule.id, required: entry.rule.count, allocated: entry.available })) }
  }

  // ---------------------------------------------------------------- papers
  const papersOf = (activityId: string, participantId: string) => [...papers.values()]
    .filter(paper => paper.activityId === activityId && paper.participantId === participantId)
    .sort((left, right) => left.attempt - right.attempt)
  function participantFor(activity: Data, user: Data): string | null {
    if (!activity.competitionId) return user.userId
    const team = state.teams.find(candidate => candidate.competitionId === activity.competitionId
      && candidate.memberIds?.includes(user.userId) && candidate.registrationStatus === 'Approved' && !candidate.isBanned)
    return team?.id ?? null
  }
  function draw(activity: Data, participantId: string): Data[] {
    const seen = new Set(papersOf(activity.id, participantId).flatMap(paper => paper.items.map((item: Data) => item.questionId)))
    const drawn: Data[] = []
    const used = new Set<string>()
    for (const rule of activity.rules) {
      const pool = poolFor(rule).filter(question => !used.has(question.id)
        && (activity.allowExposedQuestions || !seen.has(question.id)))
      if (!pool.length) continue
      const offset = papersOf(activity.id, participantId).length * rule.count
      for (let index = 0; index < rule.count; index++) {
        const question = pool[(offset + index) % pool.length]
        if (!question || used.has(question.id)) continue
        used.add(question.id)
        drawn.push({ question, points: rule.points })
      }
    }
    return drawn
  }
  function createPaper(activity: Data, participantId: string, openedAt: string): Data {
    const participant = state.teams.find(team => team.id === participantId)
    const paper: Data = { id: id(44, ++paperCounter), activityId: activity.id, participantId,
      userId: participant ? null : participantId, teamId: participant ? participantId : null,
      attempt: papersOf(activity.id, participantId).length + 1, state: 0, openedAt, submittedAt: null,
      submissionKind: null, score: null, answerRevision: crypto.randomUUID(), items: [] as Data[] }
    paper.items = draw(activity, participantId).map((entry, index) => {
      const question: Data = entry.question
      question.paperReferences = (question.paperReferences ?? 0) + 1
      question.lastIssuedAt = now()
      question.firstExposedAt ??= now()
      return { id: id(45, paperCounter * 100 + index + 1), questionId: question.id, revisionId: question.revisionId,
        order: index, kind: question.kind, body: question.body, explanation: question.explanation ?? null, points: entry.points,
        options: (question.options as Data[]).map((option, optionIndex) => ({ id: option.id, order: optionIndex,
          content: option.content, truthValue: option.truthValue ?? null, isCorrect: option.isCorrect === true })),
        selected: [] as string[], answerRevision: crypto.randomUUID(), result: null, fullCredit: false,
        answeredById: null, answeredAt: null, answeredByName: null }
    })
    papers.set(paper.id, paper)
    return paper
  }
  function gradeItem(item: Data) {
    const correct = item.options.filter((option: Data) => option.isCorrect).map((option: Data) => option.id)
    if (item.fullCredit) item.result = 3
    else if (!item.selected.length) item.result = 2
    else item.result = sameAnswer(item.selected, correct) ? 0 : 1
  }
  function gradePaper(paper: Data) {
    for (const item of paper.items) gradeItem(item)
    paper.score = paper.items.reduce((total: number, item: Data) => total + (item.result === 0 || item.result === 3 ? item.points : 0), 0)
    return paper
  }
  function itemView(item: Data) {
    return { id: item.id, answerRevision: item.answerRevision, order: item.order, kind: item.kind, body: item.body,
      points: item.points, answeredById: item.answeredById ?? null, answeredAt: item.answeredAt ?? null,
      answeredByName: item.answeredById ? userName(item.answeredById) : null,
      options: item.options.map((option: Data) => ({ id: option.id, order: option.order, content: option.content,
        truthValue: option.truthValue ?? null, selected: item.selected.includes(option.id) })) }
  }
  const paperView = (paper: Data) => ({ id: paper.id, answerRevision: paper.answerRevision, attempt: paper.attempt,
    state: paper.state, openedAt: paper.openedAt, submittedAt: paper.submittedAt ?? null,
    submissionKind: paper.submissionKind ?? null, maximumPoints: paper.items.reduce((total: number, item: Data) => total + item.points, 0),
    score: paper.state === 0 ? null : paper.score, questions: paper.items.map(itemView) })
  function effectiveScore(activity: Data, participantId: string) {
    const submitted = papersOf(activity.id, participantId).filter(paper => paper.state === 1)
    if (!submitted.length) return { points: 0, achievedAt: null, hasSubmission: false }
    if (activity.scoringMode === 2) return { points: submitted.reduce((total: number, paper: Data) => total + paper.score, 0),
      achievedAt: submitted.at(-1)!.submittedAt, hasSubmission: true }
    const chosen = activity.scoringMode === 1 ? submitted.at(-1)!
      : submitted.reduce((best: Data, paper: Data) => (paper.score >= best.score ? paper : best), submitted[0])
    return { points: chosen.score, achievedAt: chosen.submittedAt, hasSubmission: true }
  }
  function paperViewFor(activity: Data, user: Data) {
    const participantId = participantFor(activity, user)
    if (!participantId) return null
    const own = papersOf(activity.id, participantId)
    const paper = own.at(-1) ?? null
    const limit = activity.attemptLimit
    const open = own.some(candidate => candidate.state === 0)
    const withinWindow = Date.now() >= Date.parse(activity.opensAt) && Date.now() < Date.parse(activity.closesAt)
    const deadline = paperDeadline(activity, paper)
    const canIssue = activity.state === 1 && !activity.paused && !activity.issuanceStopped && withinWindow
      && !open && (limit == null || own.length < limit)
    return { activity: activitySummary(activity), deadline, paper: paper ? paperView(paper) : null,
      issuedCount: own.length, effectiveScore: effectiveScore(activity, participantId),
      history: own.map((candidate: Data) => ({ paperId: candidate.id, attempt: candidate.attempt, state: candidate.state,
        score: candidate.state === 2 ? null : candidate.score, submittedAt: candidate.submittedAt ?? null })),
      serverTime: now(), canIssue,
      canAnswer: activity.state === 1 && !activity.paused && !activity.issuanceStopped && paper?.state === 0
        && Date.now() < Date.parse(deadline.at) }
  }
  const participantName = (paper: Data) =>
    (paper.teamId ? state.teams.find(team => team.id === paper.teamId)?.name : undefined) ?? userName(paper.participantId)
  const reviewRow = (activity: Data, paper: Data) => ({ paperId: paper.id, participantId: paper.participantId,
    userId: paper.userId ?? null, teamId: paper.teamId ?? null, name: participantName(paper), attempt: paper.attempt,
    state: paper.state, openedAt: paper.openedAt, submittedAt: paper.submittedAt ?? null, score: paper.state === 0 ? null : paper.score,
    questionCount: paper.items.length, answeredCount: paper.items.filter((item: Data) => item.selected.length > 0).length,
    submissionKind: paper.submissionKind ?? null, deadline: paperDeadline(activity, paper),
    effectiveScore: effectiveScore(activity, paper.participantId) })
  const reviewQuestion = (item: Data) => ({ question: itemView(item), revisionId: item.revisionId,
    correctOptions: item.options.filter((option: Data) => option.isCorrect).map((option: Data) => option.id),
    explanation: item.explanation ?? null, result: item.result ?? null })

  function audit(activity: Data, kind: number, user: Data, reason: string | null) {
    activity.audit.unshift({ id: crypto.randomUUID(), kind, actorName: user.userName, occurredAt: now(), reason })
  }
  const allowedOperations = (activity: Data) => {
    if (activity.state === 0) return [0]
    if (activity.state === 1) return [1, activity.paused ? 3 : 2, 4, 5, 6]
    if (activity.state === 2) return [7]
    return []
  }

  // ---------------------------------------------------------------- seeds: papers
  function seedSubmittedPaper(activity: Data, participantId: string, variant: number, submittedAt: string) {
    const paper = createPaper(activity, participantId, new Date(Date.parse(submittedAt) - activity.durationSeconds * 500).toISOString())
    paper.items.forEach((item: Data, index: number) => {
      if ((index + variant) % 4 === 0) item.selected = []
      else if ((index + variant) % 3 === 0) item.selected = [item.options.find((option: Data) => !option.isCorrect)?.id ?? item.options[0].id]
      else item.selected = item.options.filter((option: Data) => option.isCorrect).map((option: Data) => option.id)
      item.answeredById = paper.teamId
        ? state.teams.find(team => team.id === paper.teamId)?.captainId ?? null
        : paper.participantId
      item.answeredAt = submittedAt
    })
    paper.state = 1
    paper.submittedAt = submittedAt
    paper.submissionKind = 0
    return gradePaper(paper)
  }
  const practice = activities.get(id(43, 1))!
  const exam = activities.get(id(43, 2))!
  const competitionTheory = activities.get(id(43, 3))!
  seedSubmittedPaper(practice, id(1, 2), 0, date(-30))
  seedSubmittedPaper(practice, id(47, 1), 1, date(-28))
  seedSubmittedPaper(practice, id(47, 2), 2, date(-26))
  seedSubmittedPaper(exam, id(1), 0, date(-20))
  seedSubmittedPaper(exam, id(1, 2), 1, date(-18))
  seedSubmittedPaper(exam, id(1, 3), 2, date(-16))
  seedSubmittedPaper(exam, id(47, 1), 3, date(-14))
  seedSubmittedPaper(exam, id(47, 2), 4, date(-12))
  seedSubmittedPaper(exam, id(47, 3), 5, date(-10))
  seedSubmittedPaper(competitionTheory, id(5, 2), 0, date(-8))
  seedSubmittedPaper(competitionTheory, id(5, 3), 1, date(-6))

  // ---------------------------------------------------------------- mutations
  function upsertActivity(activityId: string | null, input: Data, competitionId: string | null, user: Data,
    forcedKind: number | null): Data | Response {
    const name = typeof input.name === 'string' ? input.name.trim() : ''
    if (!name) return failure(400, '活动名称必填 / Activity name is required')
    const existing = activityId ? activities.get(activityId) : null
    if (activityId && !existing) return failure(404, '演示理论活动不存在 / Theory activity not found')
    if (existing && existing.state !== 0)
      return failure(409, '已发布的活动不可修改配置 / A published activity is frozen', 'ActivityFrozen')
    const opensAt = input.opensAt ?? existing?.opensAt ?? now()
    const closesAt = input.closesAt ?? existing?.closesAt ?? date(24)
    if (!Number.isFinite(Date.parse(opensAt)) || !Number.isFinite(Date.parse(closesAt)) || Date.parse(closesAt) <= Date.parse(opensAt))
      return failure(400, '结束时间必须晚于开始时间 / The close time must be later than the open time')
    const durationSeconds = Number(input.durationSeconds ?? existing?.durationSeconds ?? 1800)
    if (!Number.isFinite(durationSeconds) || durationSeconds < 60) return failure(400, '答题时长至少 1 分钟 / At least one minute is required')
    const attemptLimit = input.attemptLimit == null ? null : Number(input.attemptLimit)
    if (attemptLimit != null && (!Number.isInteger(attemptLimit) || attemptLimit < 1)) return failure(400, '作答次数无效 / Invalid attempt limit')
    const scoringMode = Number(input.scoringMode ?? existing?.scoringMode ?? 0)
    if (![0, 1, 2].includes(scoringMode)) return failure(400, '评分策略无效 / Unknown scoring mode')
    const kind = forcedKind ?? Number(input.kind ?? existing?.kind ?? 0)
    if (![0, 1, 2].includes(kind)) return failure(400, '活动类型无效 / Unknown activity kind')
    if (attemptLimit == null && (kind !== 0 || scoringMode === 2))
      return failure(400, '仅个人练习可无限次作答 / Unlimited attempts apply only to personal practice')
    const rules: Data[] = Array.isArray(input.rules) ? input.rules : []
    if (!rules.length) return failure(400, '至少需要一条抽题规则 / At least one drawing rule is required')
    for (const rule of rules) {
      if (![0, 1, 2].includes(Number(rule.kind ?? 0))) return failure(400, '抽题题型无效 / Unknown rule kind')
      if (!Number.isInteger(Number(rule.count)) || Number(rule.count) < 1) return failure(400, '抽题数量无效 / Invalid rule count')
      if (!Number.isFinite(Number(rule.points)) || Number(rule.points) < 1) return failure(400, '分值无效 / Invalid rule points')
      if (rule.difficulty != null && ![0, 1, 2].includes(Number(rule.difficulty))) return failure(400, '难度无效 / Unknown difficulty')
    }
    const record: Data = existing ?? { id: crypto.randomUUID(), state: 0, paused: false, pausedAt: null, pausedMs: 0,
      issuanceStopped: false, audit: [], corrections: [], createdBy: user.userId, competitionId: null }
    Object.assign(record, { competitionId: competitionId ?? existing?.competitionId ?? null, name, kind, opensAt, closesAt,
      durationSeconds, attemptLimit, scoringMode, allowExposedQuestions: input.allowExposedQuestions === true,
      rules: buildRules(rules) })
    activities.set(record.id, record)
    return record
  }

  function applyOperation(activity: Data, body: Data, user: Data): Response | null {
    const action = Number(body.action)
    if (!Number.isInteger(action) || action < 0 || action > 7) return failure(400, '操作无效 / Unknown operation')
    if (!allowedOperations(activity).includes(action))
      return failure(409, '当前状态不允许该操作 / The operation is unavailable in this state', 'OperationUnavailable')
    if (action >= 6 && !String(body.reason ?? '').trim()) return failure(400, '该操作需要填写原因 / A reason is required')
    const reason = String(body.reason ?? '').trim() || null
    if (action === 0) {
      if (!activityCheck(activity).feasible) return failure(409, '题池不足，无法发布 / The pool cannot satisfy the drawing rules', 'InsufficientQuestions')
      activity.state = 1
      audit(activity, 0, user, reason)
    } else if (action === 1) {
      activity.state = 0
      audit(activity, 1, user, reason)
    } else if (action === 2) {
      activity.paused = true
      activity.pausedAt = now()
      audit(activity, 2, user, reason)
    } else if (action === 3) {
      if (activity.pausedAt) activity.pausedMs += Date.now() - Date.parse(activity.pausedAt)
      activity.paused = false
      activity.pausedAt = null
      audit(activity, 3, user, reason)
    } else if (action === 4) {
      activity.issuanceStopped = true
      audit(activity, 5, user, reason)
    } else if (action === 5) {
      const deadline = Date.parse(String(body.deadline ?? ''))
      if (!Number.isFinite(deadline) || deadline <= Date.parse(activity.closesAt))
        return failure(400, '新的截止时间必须晚于当前截止时间 / The new deadline must extend the current one')
      const cap = competitionEndOf(activity)
      if (cap != null && deadline > cap) return failure(409, '不能超过竞赛结束时间 / Extending past the competition end is not allowed', 'DeadlineCapped')
      activity.closesAt = new Date(deadline).toISOString()
      audit(activity, 4, user, reason)
    } else if (action === 6) {
      activity.state = 2
      activity.issuanceStopped = true
      audit(activity, 6, user, reason)
    } else {
      activity.state = 3
      activity.issuanceStopped = true
      audit(activity, 7, user, reason)
    }
    return null
  }

  function projectedScore(paper: Data, questionId: string, mode: number, options: string[]) {
    return paper.items.reduce((total: number, item: Data) => {
      if (item.questionId !== questionId) return total + (item.result === 0 || item.result === 3 ? item.points : 0)
      const result = item.fullCredit || mode === 1 ? 3
        : !item.selected.length ? 2 : sameAnswer(item.selected, options) ? 0 : 1
      return total + (result === 0 || result === 3 ? item.points : 0)
    }, 0)
  }
  function projectedEffectiveScore(activity: Data, participantId: string, scores: Data[]) {
    const submitted = papersOf(activity.id, participantId).filter(paper => paper.state === 1)
    const projected = submitted.map(paper => scores.find(row => row.paperId === paper.id)?.after ?? paper.score)
    if (!projected.length) return 0
    if (activity.scoringMode === 2) return projected.reduce((total, value) => total + value, 0)
    return activity.scoringMode === 1 ? projected.at(-1)! : Math.max(...projected)
  }

  // ---------------------------------------------------------------- routes
  function requireStaff(user: Data): Response | null {
    return isStaff(user) ? null : failure(403, '需要 Mock 管理账号 / Staff account required')
  }

  async function handle(request: Request, route: string, p: Data, body: Data, user: Data | undefined,
    operation: Data): Promise<Response | null> {
    if (route !== '/competitions/{competitionId}/theory' && !route.startsWith('/theory/')) return null
    if (!user) return failure(401, '请先登录演示账号 / Sign in to the Mock site')
    const url = new URL(request.url)

    if (route === '/competitions/{competitionId}/theory') {
      const competitionId = p.competitionId as string
      const linked = [...activities.values()].filter(activity => activity.competitionId === competitionId)
      if (request.method === 'GET') {
        const staff = url.searchParams.get('staff') === 'true'
        if (staff && !isStaff(user)) return failure(403, '需要 Mock 管理账号 / Staff account required')
        const visible = linked.filter(activity => staff || (activity.state !== 0 && activity.state !== 3))
          .sort((left, right) => String(right.opensAt).localeCompare(String(left.opensAt)))
        return respond(operation, visible.map(activitySummary))
      }
      if (request.method === 'PUT') {
        const denied = requireStaff(user)
        if (denied) return denied
        const saved = upsertActivity(body.activityId ?? null, body.input ?? {}, competitionId, user, 2)
        return saved instanceof Response ? saved : respond(operation, activitySummary(saved))
      }
      return failure(405, 'Mock method not allowed')
    }

    if (route === '/theory/questions') {
      if (request.method === 'GET') {
        const keyword = (url.searchParams.get('keyword') ?? '').toLowerCase()
        const knowledge = (url.searchParams.get('knowledge') ?? '').toLowerCase()
        const tag = (url.searchParams.get('tag') ?? '').toLowerCase()
        const numeric = (key: string) => { const value = url.searchParams.get(key); return value === null || value === '' ? null : Number(value) }
        const kind = numeric('kind'), stateFilter = numeric('state'), difficulty = numeric('difficulty')
        const rows = [...questions.values()]
          .filter(question => isStaff(user) || question.ownerId === user.userId || question.grants.has(user.userId))
          .filter(question => kind === null || question.kind === kind)
          .filter(question => stateFilter === null || question.state === stateFilter)
          .filter(question => difficulty === null || question.difficulty === difficulty)
          .filter(question => !knowledge || question.classifications.some((item: Data) => item.kind === 0 && item.name.toLowerCase().includes(knowledge)))
          .filter(question => !tag || question.classifications.some((item: Data) => item.kind === 1 && item.name.toLowerCase().includes(tag)))
          .filter(question => !keyword || [question.title, question.body, ...question.classifications.map((item: Data) => item.name)]
            .some((value: string) => String(value).toLowerCase().includes(keyword)))
        const descending = url.searchParams.get('desc') !== 'false'
        const sorted = rows.sort((left, right) => (descending ? -1 : 1)
          * String(left.updatedAt).localeCompare(String(right.updatedAt)) || String(left.id).localeCompare(String(right.id)))
        const offset = Math.max(0, Number(url.searchParams.get('offset') ?? 0) || 0)
        const limit = Math.max(1, Math.min(200, Number(url.searchParams.get('limit') ?? 20) || 20))
        return respond(operation, { items: sorted.slice(offset, offset + limit).map(question => questionSummary(question, user)), total: sorted.length })
      }
      if (request.method === 'POST') {
        const denied = requireStaff(user)
        if (denied) return denied
        const content: Data = body.content ?? {}
        const invalid = validateContent(content)
        if (invalid) return failure(400, invalid, 'TheoryQuestionInvalid')
        const questionId = crypto.randomUUID()
        const question: Data = { id: questionId, ownerId: user.userId, sourceId: questionId, version: 1,
          revisionId: crypto.randomUUID(), title: '', body: '', explanation: null, kind: 0, difficulty: 0, state: 0,
          updatedAt: now(), firstExposedAt: null, paperReferences: 0, lastIssuedAt: null, classifications: [],
          options: [], fileIds: [], grants: new Map<string, number>() }
        applyContent(question, content)
        questions.set(question.id, question)
        return respond(operation, { questionId: question.id, revisionId: question.revisionId, version: question.version, sourceId: question.sourceId })
      }
      return failure(405, 'Mock method not allowed')
    }

    const question = questions.get(p.questionId)
    if (route.startsWith('/theory/questions/') && !question) return failure(404, '演示理论题目不存在 / Theory question not found')

    if (route === '/theory/questions/{questionId}') {
      if (request.method === 'GET') {
        if (!canReadAnswers(question!, user)) return failure(403, '需要编辑权限才能读取答案 / Editing permission required')
        return respond(operation, questionContent(question!, user))
      }
      if (request.method === 'PUT') {
        if (!canReadAnswers(question!, user)) return failure(403, '需要编辑权限 / Editing permission required')
        const content: Data = body.content ?? {}
        const invalid = validateContent(content)
        if (invalid) return failure(400, invalid, 'TheoryQuestionInvalid')
        applyContent(question!, content)
        question!.version = Number(question!.version) + 1
        question!.revisionId = crypto.randomUUID()
        question!.updatedAt = now()
        return respond(operation, { questionId: question!.id, revisionId: question!.revisionId, version: question!.version, sourceId: question!.sourceId })
      }
      return failure(405, 'Mock method not allowed')
    }
    if (route === '/theory/questions/{questionId}/copy') {
      if (!canManageQuestion(question!, user)) return failure(403, '需要管理权限 / Management permission required')
      const copyId = crypto.randomUUID()
      const copy: Data = { ...structuredClone(question!), id: copyId, sourceId: question!.sourceId, ownerId: user.userId,
        state: 0, version: 1, revisionId: crypto.randomUUID(), updatedAt: now(), paperReferences: 0,
        firstExposedAt: null, lastIssuedAt: null, grants: new Map<string, number>() }
      copy.options = question!.options.map((option: Data) => ({ ...option, id: crypto.randomUUID() }))
      questions.set(copyId, copy)
      return respond(operation, { questionId: copy.id, revisionId: copy.revisionId, version: copy.version, sourceId: copy.sourceId })
    }
    if (route === '/theory/questions/{questionId}/state') {
      if (!canManageQuestion(question!, user)) return failure(403, '需要管理权限 / Management permission required')
      const next = Number(body.state)
      if (![0, 1, 2].includes(next)) return failure(400, '题目状态无效 / Unknown question state')
      question!.state = next
      question!.updatedAt = now()
      return respond(operation, null, 204)
    }
    if (route === '/theory/questions/{questionId}/grants/{userId}') {
      if (!canManageQuestion(question!, user)) return failure(403, '需要管理权限 / Management permission required')
      const target = state.users.find(candidate => candidate.userId === p.userId)
      if (!target) return failure(404, '演示账号不存在 / Mock user not found')
      if (body.permission == null) question!.grants.delete(p.userId)
      else {
        const permission = Number(body.permission)
        if (![0, 1, 2].includes(permission)) return failure(400, '权限无效 / Unknown permission')
        if (target.userId === question!.ownerId) return failure(400, '所有者权限无需授予 / The owner already has full access')
        question!.grants.set(p.userId, permission)
      }
      return respond(operation, null, 204)
    }
    if (route === '/theory/questions/{questionId}/images') {
      if (!canReadAnswers(question!, user)) return failure(403, '需要编辑权限 / Editing permission required')
      const form = await request.formData()
      const file = form.get('file')
      if (!(file instanceof Blob) || !['image/png', 'image/jpeg', 'image/webp'].includes(file.type))
        return json({ status: 400, title: 'Mock API', code: 'UnsupportedFormat', detail: '仅支持 PNG/JPEG/WebP / PNG, JPEG or WebP only' }, 400)
      if (file.size > maximumImageBytes)
        return json({ status: 400, title: 'Mock API', code: 'FileTooLarge', detail: '图片超过 5 MiB / Image exceeds 5 MiB' }, 400)
      const fileId = crypto.randomUUID()
      questionImages.set(fileId, { blob: file, questionId: question!.id })
      question!.fileIds.push(fileId)
      question!.updatedAt = now()
      return respond(operation, { fileId, revisionId: question!.revisionId, failure: null })
    }
    if (route === '/theory/questions/{questionId}/images/{fileId}') {
      if (!canReadAnswers(question!, user)) return failure(403, '需要读取权限 / Read permission required')
      const image = questionImages.get(p.fileId)
      if (!image || image.questionId !== question!.id) return failure(404, '演示图片不存在 / Theory image not found')
      return new Response(image.blob, { status: 200, headers: { 'Content-Type': image.blob.type || 'image/png',
        'Cache-Control': 'no-store', 'X-NoCTF-Mock': 'true' } })
    }

    if (route === '/theory/activities') {
      if (request.method === 'GET') {
        const staff = url.searchParams.get('staff') === 'true'
        if (staff && !isStaff(user)) return failure(403, '需要 Mock 管理账号 / Staff account required')
        const competitionId = url.searchParams.get('competitionId')
        const keyword = (url.searchParams.get('keyword') ?? '').toLowerCase()
        const rows = [...activities.values()]
          .filter(activity => !competitionId || activity.competitionId === competitionId)
          .filter(activity => staff || (activity.state !== 0 && activity.state !== 3))
          .filter(activity => !keyword || activity.name.toLowerCase().includes(keyword))
        const descending = url.searchParams.get('desc') !== 'false'
        const sorted = rows.sort((left, right) => (descending ? -1 : 1) * String(left.opensAt).localeCompare(String(right.opensAt)))
        const offset = Math.max(0, Number(url.searchParams.get('offset') ?? 0) || 0)
        const limit = Math.max(1, Math.min(200, Number(url.searchParams.get('limit') ?? 20) || 20))
        return respond(operation, { items: sorted.slice(offset, offset + limit).map(activitySummary), total: sorted.length })
      }
      if (request.method === 'POST') {
        const denied = requireStaff(user)
        if (denied) return denied
        const saved = upsertActivity(body.activityId ?? null, body.input ?? {}, body.competitionId ?? null, user, null)
        return saved instanceof Response ? saved : respond(operation, activitySummary(saved))
      }
      return failure(405, 'Mock method not allowed')
    }

    const activity = activities.get(p.activityId)
    if (route.startsWith('/theory/activities/') && !activity) return failure(404, '演示理论活动不存在 / Theory activity not found')

    if (route === '/theory/activities/{activityId}/management') {
      const denied = requireStaff(user)
      if (denied) return denied
      return respond(operation, { activity: activitySummary(activity!), rules: activity!.rules.map((rule: Data) => ({
        kind: rule.kind, difficulty: rule.difficulty, count: rule.count, points: rule.points, filters: rule.filters })),
        allowExposedQuestions: activity!.allowExposedQuestions, canManage: true, canJudge: true,
        deadline: activityDeadline(activity!), corrections: activity!.corrections,
        issuedCount: [...papers.values()].filter(paper => paper.activityId === activity!.id).length,
        competitionEndAt: activity!.competitionId ? new Date(competitionEndOf(activity!)!).toISOString() : null,
        allowedOperations: allowedOperations(activity!) })
    }
    if (route === '/theory/activities/{activityId}/check') {
      const denied = requireStaff(user)
      if (denied) return denied
      return respond(operation, activityCheck(activity!))
    }
    if (route === '/theory/activities/{activityId}/operations') {
      const denied = requireStaff(user)
      if (denied) return denied
      const refused = applyOperation(activity!, body, user)
      if (refused) return refused
      changed(activity!.id)
      return respond(operation, activitySummary(activity!))
    }
    if (route === '/theory/activities/{activityId}/audit') {
      const denied = requireStaff(user)
      if (denied) return denied
      const offset = Math.max(0, Number(url.searchParams.get('offset') ?? 0) || 0)
      const limit = Math.max(1, Math.min(200, Number(url.searchParams.get('limit') ?? 20) || 20))
      const descending = url.searchParams.get('desc') !== 'false'
      const rows = [...activity!.audit].sort((left: Data, right: Data) => (descending ? -1 : 1)
        * String(left.occurredAt).localeCompare(String(right.occurredAt)))
      return respond(operation, { items: rows.slice(offset, offset + limit), total: rows.length })
    }
    if (route === '/theory/activities/{activityId}/session') {
      if (!isStaff(user) && activity!.state !== 1 && activity!.state !== 2)
        return failure(404, '演示理论活动不存在 / Theory activity not found')
      const view = paperViewFor(activity!, user)
      if (!view) return failure(403, '需要已审核队伍才能作答 / An approved team is required')
      return respond(operation, view)
    }
    if (route === '/theory/activities/{activityId}/papers') {
      const participantId = participantFor(activity!, user)
      if (!participantId) return failure(403, '需要已审核队伍才能作答 / An approved team is required')
      const requestId = String(body.requestId ?? '')
      const replay = issueReceipts.get(requestId)
      if (replay && papers.has(replay)) return respond(operation, paperViewFor(activity!, user))
      const own = papersOf(activity!.id, participantId)
      if (body.expectedIssuedCount != null && Number(body.expectedIssuedCount) !== own.length)
        return failure(409, '已发放试卷数量已变化 / Issued paper count changed', 'IssueConflict')
      if (activity!.state !== 1) return failure(409, '活动未开放 / Activity is not published', 'ActivityClosed')
      if (activity!.paused) return failure(409, '作答已暂停 / Answering is paused', 'ActivityPaused')
      if (activity!.issuanceStopped) return failure(409, '已停止发放试卷 / Issuance stopped', 'IssuanceStopped')
      if (Date.now() < Date.parse(activity!.opensAt) || Date.now() >= Date.parse(activity!.closesAt))
        return failure(409, '不在作答时间内 / Outside the answering window', 'ActivityClosed')
      if (own.some(paper => paper.state === 0)) return failure(409, '已有未提交的试卷 / An open paper already exists', 'PaperOpen')
      if (activity!.attemptLimit != null && own.length >= activity!.attemptLimit)
        return failure(409, '已达到作答次数上限 / Attempt limit reached', 'AttemptLimitReached')
      if (!activityCheck(activity!).feasible)
        return failure(409, '题池不足，无法组卷 / The pool cannot satisfy the drawing rules', 'InsufficientQuestions')
      const paper = createPaper(activity!, participantId, now())
      if (!paper.items.length) { papers.delete(paper.id); return failure(409, '题池不足，无法组卷 / The pool cannot satisfy the drawing rules', 'InsufficientQuestions') }
      issueReceipts.set(requestId, paper.id)
      audit(activity!, 1, user, '发放第 ' + paper.attempt + ' 次试卷 / Issued attempt ' + paper.attempt)
      changed(activity!.id)
      return respond(operation, paperViewFor(activity!, user))
    }
    if (route === '/theory/activities/{activityId}/papers/{paperId}/answers/{itemId}') {
      const paper = papers.get(p.paperId)
      if (!paper || paper.activityId !== activity!.id) return failure(404, '演示试卷不存在 / Theory paper not found')
      if (!isStaff(user) && paper.participantId !== participantFor(activity!, user))
        return failure(403, '无法作答他人试卷 / This paper belongs to another participant')
      const replay = answerReceipts.get(String(body.requestId ?? ''))
      if (replay && replay.paperId === paper.id && replay.itemId === p.itemId)
        return respond(operation, paperViewFor(activity!, user))
      if (paper.state !== 0) return failure(409, '试卷已提交 / The paper was already submitted', 'PaperClosed')
      if (!paperViewFor(activity!, user)?.canAnswer) return failure(403, '当前无法作答 / Answering is closed', 'AnsweringClosed')
      const item = paper.items.find((candidate: Data) => candidate.id === p.itemId)
      if (!item) return failure(404, '演示题目不存在 / Paper item not found')
      const optionIds: string[] = Array.isArray(body.optionIds) ? body.optionIds.map(String) : []
      const valid = new Set(item.options.map((option: Data) => option.id))
      if (optionIds.some(optionId => !valid.has(optionId))) return failure(400, '选项不属于该题 / Unknown option', 'AnswerInvalid')
      if (item.kind !== 1 && optionIds.length > 1) return failure(400, '单选题只能选择一个选项 / Single choice allows one option', 'AnswerInvalid')
      if (body.expectedRevision !== item.answerRevision)
        return failure(409, '答案已被他人更新 / The answer was updated elsewhere', 'AnswerConflict')
      item.selected = [...new Set(optionIds)]
      item.answerRevision = crypto.randomUUID()
      if (item.selected.length) { item.answeredById = user.userId; item.answeredAt = now(); item.answeredByName = user.userName }
      else { item.answeredById = null; item.answeredAt = null; item.answeredByName = null }
      paper.answerRevision = crypto.randomUUID()
      answerReceipts.set(String(body.requestId ?? ''), { paperId: paper.id, itemId: item.id })
      changed(activity!.id)
      return respond(operation, paperViewFor(activity!, user))
    }
    if (route === '/theory/activities/{activityId}/papers/{paperId}/submit') {
      const paper = papers.get(p.paperId)
      if (!paper || paper.activityId !== activity!.id) return failure(404, '演示试卷不存在 / Theory paper not found')
      if (!isStaff(user) && paper.participantId !== participantFor(activity!, user))
        return failure(403, '无法提交他人试卷 / This paper belongs to another participant')
      if (paper.state !== 0) return failure(409, '试卷已提交 / The paper was already submitted', 'PaperConflict')
      if (body.expectedRevision !== paper.answerRevision)
        return failure(409, '试卷已更新，请刷新后重试 / The paper changed, reload and retry', 'PaperConflict')
      paper.state = 1
      paper.submittedAt = now()
      paper.submissionKind = 0
      gradePaper(paper)
      audit(activity!, 6, user, '提交第 ' + paper.attempt + ' 次试卷 / Submitted attempt ' + paper.attempt)
      changed(activity!.id)
      return respond(operation, paperViewFor(activity!, user))
    }
    if (route === '/theory/activities/{activityId}/papers/{paperId}') {
      const paper = papers.get(p.paperId)
      if (!paper || paper.activityId !== activity!.id) return failure(404, '演示试卷不存在 / Theory paper not found')
      if (!isStaff(user) && paper.participantId !== participantFor(activity!, user))
        return failure(403, '无法读取他人试卷 / This paper belongs to another participant')
      return respond(operation, paperView(paper))
    }
    if (route === '/theory/activities/{activityId}/papers/{paperId}/images/{fileId}') {
      const paper = papers.get(p.paperId)
      if (!paper || paper.activityId !== activity!.id) return failure(404, '演示试卷不存在 / Theory paper not found')
      const staffViewer = isStaff(user) && url.searchParams.get('staff') === 'true'
      if (!staffViewer && paper.participantId !== participantFor(activity!, user))
        return failure(403, '无法读取该图片 / Image access denied')
      const referenced = paper.items.some((item: Data) => questions.get(item.questionId)?.fileIds?.includes(p.fileId))
      const image = questionImages.get(p.fileId)
      if (!referenced || !image) return failure(404, '演示图片不存在 / Theory image not found')
      return new Response(image.blob, { status: 200, headers: { 'Content-Type': image.blob.type || 'image/png',
        'Cache-Control': 'private,no-store', 'X-NoCTF-Mock': 'true' } })
    }
    if (route === '/theory/activities/{activityId}/reviews') {
      const denied = requireStaff(user)
      if (denied) return denied
      const offset = Math.max(0, Number(url.searchParams.get('offset') ?? 0) || 0)
      const limit = Math.max(1, Math.min(200, Number(url.searchParams.get('limit') ?? 20) || 20))
      const descending = url.searchParams.get('desc') !== 'false'
      const rows = [...papers.values()].filter(paper => paper.activityId === activity!.id)
        .sort((left, right) => (descending ? -1 : 1) * String(left.openedAt).localeCompare(String(right.openedAt)))
      return respond(operation, { items: rows.slice(offset, offset + limit).map(paper => reviewRow(activity!, paper)), total: rows.length })
    }
    if (route === '/theory/activities/{activityId}/reviews/{paperId}') {
      const denied = requireStaff(user)
      if (denied) return denied
      const paper = papers.get(p.paperId)
      if (!paper || paper.activityId !== activity!.id) return failure(404, '演示试卷不存在 / Theory paper not found')
      return respond(operation, { paper: reviewRow(activity!, paper), questions: paper.items.map(reviewQuestion) })
    }
    if (route === '/theory/activities/{activityId}/reviews/{paperId}/void') {
      const denied = requireStaff(user)
      if (denied) return denied
      const paper = papers.get(p.paperId)
      if (!paper || paper.activityId !== activity!.id) return failure(404, '演示试卷不存在 / Theory paper not found')
      if (!String(body.reason ?? '').trim()) return failure(400, '作废原因必填 / A void reason is required')
      if (paper.state === 2) return failure(409, '试卷已作废 / The paper was already voided', 'PaperVoided')
      paper.state = 2
      paper.score = null
      audit(activity!, 9, user, String(body.reason).trim())
      changed(activity!.id)
      return respond(operation, null, 204)
    }
    if (route === '/theory/activities/{activityId}/reviews/{paperId}/consultations') {
      const denied = requireStaff(user)
      if (denied) return denied
      if (!activity!.competitionId) return failure(404, '仅竞赛理论支持咨询 / Consultations require a competition activity')
      const paper = papers.get(p.paperId)
      if (!paper || paper.activityId !== activity!.id) return failure(404, '演示试卷不存在 / Theory paper not found')
      if (typeof body.body !== 'string' || !body.body.trim()) return failure(400, '咨询内容必填 / Consultation body is required')
      const rootId = crypto.randomUUID()
      const team = paper.teamId ? state.teams.find(candidate => candidate.id === paper.teamId) : undefined
      state.questions.unshift(model('QuestionsCompetitionQuestionResponse', { subject: 'Platform',
        title: '理论答卷咨询 / Theory paper consultation', body: body.body.trim(), threadRootId: rootId,
        competitionId: activity!.competitionId, teamId: paper.teamId ?? null, askedByUserId: user.userId,
        askerDisplayName: user.userName, teamDisplayName: team?.name ?? null, status: 'Pending',
        createdAt: now(), updatedAt: now(), maxParticipantMessagesBeforeHandlerReply: 5,
        lastActorDisplayName: user.userName, lastActorRole: 'PlatformAdministrator',
        entries: [model('QuestionsCompetitionQuestionEntryResponse', { id: rootId, kind: 'Message',
          actorRole: 'PlatformAdministrator', actorUserId: user.userId, actorDisplayName: user.userName,
          body: body.body.trim(), createdAt: now() })] }))
      return respond(operation, { threadRootId: rootId })
    }
    if (route === '/theory/activities/{activityId}/leaderboard') {
      const offset = Math.max(0, Number(url.searchParams.get('offset') ?? 0) || 0)
      const limit = Math.max(1, Math.min(200, Number(url.searchParams.get('limit') ?? 20) || 20))
      const participants = [...new Set([...papers.values()].filter(paper => paper.activityId === activity!.id)
        .map(paper => paper.participantId))]
      const ranked = participants.map(participantId => ({ participantId, ...effectiveScore(activity!, participantId) }))
        .sort((left, right) => right.points - left.points || String(left.participantId).localeCompare(String(right.participantId)))
      const items = ranked.slice(offset, offset + limit).map((row, index) => ({ rank: offset + index + 1, userId: row.participantId,
        name: participantName({ participantId: row.participantId,
          teamId: state.teams.some(team => team.id === row.participantId) ? row.participantId : null }),
        points: row.points, achievedAt: row.achievedAt ?? null, hasSubmission: row.hasSubmission }))
      return respond(operation, { items, total: ranked.length })
    }
    if (route === '/theory/activities/{activityId}/corrections/preview') {
      const denied = requireStaff(user)
      if (denied) return denied
      const input: Data = body.input ?? {}
      const target = [...questions.values()].find(candidate => candidate.revisionId === input.revisionId)
      if (!target) return failure(404, '题目修订不存在 / Question revision not found')
      const mode = Number(input.mode ?? 0)
      if (![0, 1].includes(mode)) return failure(400, '批改模式无效 / Unknown correction mode')
      const options: string[] = Array.isArray(input.correctOptions) ? input.correctOptions.map(String) : []
      if (mode === 0) {
        if (!options.length) return failure(400, '请选择正确答案 / Select at least one correct option')
        if (options.some(optionId => !target.options.some((option: Data) => option.id === optionId)))
          return failure(400, '选项不属于该题 / Unknown option for this question')
      }
      const impacted = [...papers.values()].filter(paper => paper.activityId === activity!.id
        && paper.items.some((item: Data) => item.questionId === target.id))
      const scores = impacted.map(paper => ({ paperId: paper.id, participantId: paper.participantId,
        before: paper.score ?? 0, after: projectedScore(paper, target.id, mode, options) }))
      const participants = [...new Set(scores.map(row => row.participantId))].map(participantId => ({
        participantId,
        name: participantName({ participantId,
          teamId: state.teams.some(team => team.id === participantId) ? participantId : null }),
        before: effectiveScore(activity!, participantId).points,
        after: projectedEffectiveScore(activity!, participantId, scores) }))
      const token = crypto.randomUUID()
      previews.set(token, { revisionId: String(input.revisionId), mode, correctOptions: options,
        activityId: activity!.id, questionId: target.id })
      return respond(operation, { token, expiresAt: date(0.25), affectedPapers: impacted.length, scores, participants })
    }
    if (route === '/theory/activities/{activityId}/corrections') {
      const denied = requireStaff(user)
      if (denied) return denied
      const input: Data = body.input ?? {}
      const preview = previews.get(String(body.token ?? ''))
      if (!preview || preview.activityId !== activity!.id)
        return failure(409, '批改预览已失效 / The correction preview expired', 'CorrectionExpired')
      if (preview.revisionId !== input.revisionId || preview.mode !== Number(input.mode ?? 0)
        || JSON.stringify(preview.correctOptions) !== JSON.stringify((input.correctOptions ?? []).map(String)))
        return failure(409, '批改内容与预览不一致 / The correction differs from the preview', 'CorrectionMismatch')
      if (typeof body.reason !== 'string' || !body.reason.trim()) return failure(400, '批改原因必填 / A correction reason is required')
      const target = questions.get(preview.questionId)!
      const previousRevision = target.revisionId
      const mode = Number(preview.mode)
      const correctOptions: string[] = preview.correctOptions
      target.revisionId = crypto.randomUUID()
      target.version = Number(target.version) + 1
      target.updatedAt = now()
      if (mode === 0) target.options.forEach((option: Data) => { option.isCorrect = correctOptions.includes(option.id) })
      for (const paper of papers.values()) {
        if (paper.activityId !== activity!.id) continue
        let touched = false
        for (const item of paper.items) {
          if (item.questionId !== target.id || item.revisionId !== previousRevision) continue
          item.revisionId = target.revisionId
          if (mode === 0) item.options.forEach((option: Data) => { option.isCorrect = correctOptions.includes(option.id) })
          else item.fullCredit = true
          touched = true
        }
        if (touched) gradePaper(paper)
      }
      activity!.corrections.unshift({ revisionId: previousRevision, targetRevision: target.revisionId, completed: true })
      previews.delete(String(body.token))
      audit(activity!, mode === 0 ? 10 : 11, user, body.reason.trim())
      changed(activity!.id)
      return respond(operation, { targetRevision: target.revisionId,
        statusUrl: '/api/v1/theory/activities/' + activity!.id + '/management' }, 202)
    }
    return failure(404, 'Mock route not found')
  }

  const hasPublishedTheory = (competitionId: string) =>
    [...activities.values()].some(activity => activity.competitionId === competitionId && activity.state === 1)

  return { handle, changes, hasPublishedTheory, questions, activities, papers, questionImages }
}
