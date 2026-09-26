import { describe, expect, test } from 'bun:test'
import { sourceFile } from './support/feature-source'

describe('route loading boundaries', () => {
  test('keeps feature-specific surfaces out of the global stylesheet', async () => {
    const globalCss = await sourceFile('app/assets/css/main.css').text()
    const markdown = await sourceFile('app/components/ui/markdown/MarkdownContent.vue').text()
    const pdf = await sourceFile('app/components/ui/pdf-preview/PdfPreview.vue').text()
    const workspace = await sourceFile('app/components/views/app/AppWorkspaceNavView.vue').text()
    const login = await sourceFile('app/components/views/page/auth/AuthLoginPageView.vue').text()

    expect(globalCss).not.toContain('.markdown-content {')
    expect(globalCss).not.toContain("[data-slot='pdf-preview-toolbar'] {")
    expect(globalCss).not.toContain('.settings-workspace-page {')
    expect(globalCss).not.toContain('.auth-card {')
    expect(markdown).toContain('<style src="./markdown.css"></style>')
    expect(pdf).toContain('<style src="./pdf-preview.css"></style>')
    expect(workspace).toContain('<style src="./settings-workspace.css"></style>')
    expect(login).toContain('<style src="./auth-artwork.css"></style>')
  })

  test('prefetches destinations in large visible lists only after user intent', async () => {
    const challengeLibrary = await sourceFile('app/components/views/page/admin/challenges/AdminChallengesIndexPageView.vue').text()
    const competitionChallenges = await sourceFile('app/components/views/page/admin/competitions/[id]/challenges/AdminCompetitionsByIdChallengesIndexPageView.vue').text()
    const teams = await sourceFile('app/components/views/page/competitions/[id]/teams/CompetitionsByIdTeamsIndexPageView.vue').text()

    expect(challengeLibrary).toMatch(/v-for="template in filteredTemplates"[\s\S]+prefetch-on="interaction"/)
    expect(competitionChallenges).toMatch(/v-for="c in pageItems"[\s\S]+prefetch-on="interaction"/)
    expect(teams).toMatch(/v-for="team in teams"[\s\S]+prefetch-on="interaction"/)
  })
})
