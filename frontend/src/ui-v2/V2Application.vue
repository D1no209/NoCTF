<script setup lang="ts">
import { computed, defineAsyncComponent } from 'vue'
import { useRoute } from 'vue-router'
import V2AuthShell from './layouts/V2AuthShell.vue'
import V2RootShell from './layouts/V2RootShell.vue'
import V2PublicScreenShell from './layouts/V2PublicScreenShell.vue'
import V2AwdDashboardPage from './pages/V2AwdDashboardPage.vue'
import V2CompetitionDetailPage from './pages/V2CompetitionDetailPage.vue'
import V2CompetitionRegistrationPage from './pages/V2CompetitionRegistrationPage.vue'
import V2CompetitionsPage from './pages/V2CompetitionsPage.vue'
import V2HomePage from './pages/V2HomePage.vue'
import V2KohDashboardPage from './pages/V2KohDashboardPage.vue'
import V2LoginPage from './pages/V2LoginPage.vue'
import V2NotFoundPage from './pages/V2NotFoundPage.vue'
import V2PenetrationDashboardPage from './pages/V2PenetrationDashboardPage.vue'
import V2RegisterPage from './pages/V2RegisterPage.vue'
import V2RouteMismatchPage from './pages/V2RouteMismatchPage.vue'
import V2TeamsPage from './pages/V2TeamsPage.vue'
import V2ThemePackagesPage from './pages/V2ThemePackagesPage.vue'
import V2VerifyEmailPage from './pages/V2VerifyEmailPage.vue'
import V2AwdpScreenPage from './pages/V2AwdpScreenPage.vue'

// Admin pages are code-split — they pull in the admin API surface, SignalR,
// and heavy editor workspaces that public visitors never need.
const V2AdminUsersPage = defineAsyncComponent(() => import('./pages/V2AdminUsersPage.vue'))
const V2AdminTeamsPage = defineAsyncComponent(() => import('./pages/V2AdminTeamsPage.vue'))
const V2AdminCompetitionsPage = defineAsyncComponent(() => import('./pages/V2AdminCompetitionsPage.vue'))
const V2AdminCompetitionDetailPage = defineAsyncComponent(() => import('./pages/V2AdminCompetitionDetailPage.vue'))
const V2AdminCompetitionOperationsPage = defineAsyncComponent(() => import('./pages/V2AdminCompetitionOperationsPage.vue'))
const V2AdminCollaboratorsPage = defineAsyncComponent(() => import('./pages/V2AdminCollaboratorsPage.vue'))
const V2AdminChallengesPage = defineAsyncComponent(() => import('./pages/V2AdminChallengesPage.vue'))
const V2AdminChallengeCreatePage = defineAsyncComponent(() => import('./pages/V2AdminChallengeCreatePage.vue'))
const V2AdminContainersPage = defineAsyncComponent(() => import('./pages/V2AdminContainersPage.vue'))
const V2AdminPluginsPage = defineAsyncComponent(() => import('./pages/V2AdminPluginsPage.vue'))
const V2AdminInfrastructurePage = defineAsyncComponent(() => import('./pages/V2AdminInfrastructurePage.vue'))
const V2AdminQqBotPage = defineAsyncComponent(() => import('./pages/V2AdminQqBotPage.vue'))
const V2AdminEmailVerificationPage = defineAsyncComponent(() => import('./pages/V2AdminEmailVerificationPage.vue'))
const V2AdminAuditLogsPage = defineAsyncComponent(() => import('./pages/V2AdminAuditLogsPage.vue'))
const V2AdminHealthPage = defineAsyncComponent(() => import('./pages/V2AdminHealthPage.vue'))
const V2AdminLogsPage = defineAsyncComponent(() => import('./pages/V2AdminLogsPage.vue'))

const route = useRoute()

const currentPage = computed(() => {
  if (route.name === 'home')
    return V2HomePage
  if (route.name === 'login')
    return V2LoginPage
  if (route.name === 'register')
    return V2RegisterPage
  if (route.name === 'verify-email')
    return V2VerifyEmailPage
  if (route.name === 'admin-theme-packs')
    return V2ThemePackagesPage
  if (route.name === 'competitions')
    return V2CompetitionsPage
  if (route.name === 'competition-detail')
    return V2CompetitionDetailPage
  if (route.name === 'competition-register')
    return V2CompetitionRegistrationPage
  if (route.name === 'teams')
    return V2TeamsPage
  if (route.name === 'awdp-screen')
    return V2AwdpScreenPage
  if (route.name === 'awd-dashboard')
    return V2AwdDashboardPage
  if (route.name === 'koh-dashboard')
    return V2KohDashboardPage
  if (route.name === 'penetration-dashboard')
    return V2PenetrationDashboardPage
  if (route.name === 'admin-users')
    return V2AdminUsersPage
  if (route.name === 'admin-teams')
    return V2AdminTeamsPage
  if (route.name === 'admin-competitions')
    return V2AdminCompetitionsPage
  if (route.name === 'admin-competition-detail')
    return V2AdminCompetitionDetailPage
  if (route.name === 'admin-competition-operations')
    return V2AdminCompetitionOperationsPage
  if (route.name === 'admin-collaborators')
    return V2AdminCollaboratorsPage
  if (route.name === 'admin-challenges')
    return V2AdminChallengesPage
  if (route.name === 'admin-challenge-create')
    return V2AdminChallengeCreatePage
  if (route.name === 'admin-containers')
    return V2AdminContainersPage
  if (route.name === 'admin-plugins')
    return V2AdminPluginsPage
  if (route.name === 'admin-infrastructure')
    return V2AdminInfrastructurePage
  if (route.name === 'admin-qqbot')
    return V2AdminQqBotPage
  if (route.name === 'admin-email-verification')
    return V2AdminEmailVerificationPage
  if (route.name === 'admin-audit-logs')
    return V2AdminAuditLogsPage
  if (route.name === 'admin-health')
    return V2AdminHealthPage
  if (route.name === 'admin-logs')
    return V2AdminLogsPage
  if (route.name === 'not-found')
    return V2NotFoundPage

  return V2RouteMismatchPage
})

const isPublicScreen = computed(() => route.name === 'awdp-screen')
const isAuthRoute = computed(() =>
  route.name === 'login' || route.name === 'register' || route.name === 'verify-email')
</script>

<template>
  <V2PublicScreenShell v-if="isPublicScreen">
    <component :is="currentPage" />
  </V2PublicScreenShell>
  <V2AuthShell v-else-if="isAuthRoute">
    <component :is="currentPage" />
  </V2AuthShell>
  <V2RootShell v-else>
    <component :is="currentPage" />
  </V2RootShell>
</template>
