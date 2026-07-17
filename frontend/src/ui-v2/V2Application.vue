<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import V2RootShell from './layouts/V2RootShell.vue'
import NeonCommandPreviewPage from './pages/NeonCommandPreviewPage.vue'
import V2CompetitionDetailPage from './pages/V2CompetitionDetailPage.vue'
import V2CompetitionsPage from './pages/V2CompetitionsPage.vue'
import V2CompetitionRegistrationPage from './pages/V2CompetitionRegistrationPage.vue'
import V2TeamsPage from './pages/V2TeamsPage.vue'
import V2ThemePackagesPage from './pages/V2ThemePackagesPage.vue'
import V2RouteMismatchPage from './pages/V2RouteMismatchPage.vue'
import V2AwdpScreenPage from './pages/V2AwdpScreenPage.vue'
import V2AwdDashboardPage from './pages/V2AwdDashboardPage.vue'
import V2PublicScreenShell from './layouts/V2PublicScreenShell.vue'

const route = useRoute()

const currentPage = computed(() => {
  if (route.name === 'home')
    return NeonCommandPreviewPage
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

  return V2RouteMismatchPage
})

const isPublicScreen = computed(() => route.name === 'awdp-screen')
</script>

<template>
  <V2PublicScreenShell v-if="isPublicScreen">
    <component :is="currentPage" />
  </V2PublicScreenShell>
  <V2RootShell v-else>
    <component :is="currentPage" />
  </V2RootShell>
</template>
