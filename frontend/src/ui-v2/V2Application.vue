<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import V2AuthShell from './layouts/V2AuthShell.vue'
import V2RootShell from './layouts/V2RootShell.vue'
import V2PublicScreenShell from './layouts/V2PublicScreenShell.vue'
import NeonCommandPreviewPage from './pages/NeonCommandPreviewPage.vue'
import V2AwdDashboardPage from './pages/V2AwdDashboardPage.vue'
import V2CompetitionDetailPage from './pages/V2CompetitionDetailPage.vue'
import V2CompetitionRegistrationPage from './pages/V2CompetitionRegistrationPage.vue'
import V2CompetitionsPage from './pages/V2CompetitionsPage.vue'
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

const route = useRoute()

const currentPage = computed(() => {
  if (route.name === 'home')
    return NeonCommandPreviewPage
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
