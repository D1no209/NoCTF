import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: '/',
      name: 'home',
      component: () => import('@/ui-v1/views/HomeView.vue'),
      meta: { requiresAuth: true },
    },
    {
      path: '/login',
      name: 'login',
      component: () => import('@/ui-v1/views/LoginView.vue'),
      meta: { requiresAuth: false },
    },
    {
      path: '/register',
      name: 'register',
      component: () => import('@/ui-v1/views/RegisterView.vue'),
      meta: { requiresAuth: false },
    },
    {
      path: '/verify-email',
      name: 'verify-email',
      component: () => import('@/ui-v1/views/VerifyEmailView.vue'),
      meta: { requiresAuth: false },
    },
    {
      path: '/competitions',
      name: 'competitions',
      component: () => import('@/ui-v1/views/CompetitionsView.vue'),
      meta: { requiresAuth: true },
    },
    {
      path: '/teams',
      name: 'teams',
      component: () => import('@/ui-v1/views/TeamsView.vue'),
      meta: { requiresAuth: true },
    },
    {
      path: '/awdp/screen/:gameId',
      name: 'awdp-screen',
      component: () => import('@/ui-v1/views/AwdpScreenView.vue'),
      meta: { requiresAuth: false },
    },
    {
      path: '/competitions/:id/register',
      name: 'competition-register',
      component: () => import('@/ui-v1/views/CompetitionRegistrationView.vue'),
      meta: { requiresAuth: true },
    },
    {
      path: '/competitions/:id',
      component: () => import('@/ui-v1/components/layout/GameLayout.vue'),
      meta: { requiresAuth: true },
      children: [
        {
          path: '',
          name: 'competition-detail',
          component: () => import('@/ui-v1/views/CompetitionGatewayView.vue'),
        },
        {
          path: 'awd',
          name: 'awd-dashboard',
          component: () => import('@/ui-v1/views/AwdDashboardView.vue'),
        },
        {
          path: 'koh',
          name: 'koh-dashboard',
          component: () => import('@/ui-v1/views/KohDashboardView.vue'),
        },
        {
          path: 'penetration',
          name: 'penetration-dashboard',
          component: () => import('@/ui-v1/views/PenetrationView.vue'),
        },
      ],
    },
    {
      path: '/admin',
      component: () => import('@/ui-v1/views/admin/AdminLayout.vue'),
      meta: { requiresAuth: true, requiresAdminOrOrganizer: true },
      children: [
        {
          path: '',
          redirect: '/admin/competitions',
        },
        {
          path: 'users',
          name: 'admin-users',
          component: () => import('@/ui-v1/views/admin/AdminUsersView.vue'),
          meta: { requiresAuth: true, requiresAdminOrOrganizer: true, requiresAdmin: true },
        },
        {
          path: 'teams',
          name: 'admin-teams',
          component: () => import('@/ui-v1/views/admin/AdminTeamsView.vue'),
          meta: { requiresAuth: true, requiresAdminOrOrganizer: true },
        },
        {
          path: 'competitions',
          name: 'admin-competitions',
          component: () => import('@/ui-v1/views/admin/AdminCompetitionsView.vue'),
          meta: { requiresAuth: true, requiresAdminOrOrganizer: true },
        },
        {
          path: 'competitions/:id/challenges/new',
          name: 'admin-competition-challenge-create',
          component: () => import('@/ui-v1/views/admin/AdminCompetitionChallengeEditorView.vue'),
          meta: { requiresAuth: true, requiresAdminOrOrganizer: true },
        },
        {
          path: 'competitions/:id/challenges/:challengeId/edit',
          name: 'admin-competition-challenge-edit',
          component: () => import('@/ui-v1/views/admin/AdminCompetitionChallengeEditorView.vue'),
          meta: { requiresAuth: true, requiresAdminOrOrganizer: true },
        },
        {
          path: 'competitions/:id',
          name: 'admin-competition-detail',
          component: () => import('@/ui-v1/views/admin/AdminCompetitionDetailView.vue'),
          meta: { requiresAuth: true, requiresAdminOrOrganizer: true },
        },
        {
          path: 'competitions/:id/operations',
          name: 'admin-competition-operations',
          component: () => import('@/ui-v1/views/admin/AdminCompetitionOperationsView.vue'),
          meta: { requiresAuth: true, requiresAdminOrOrganizer: true },
        },
        {
          path: 'collaborators',
          name: 'admin-collaborators',
          component: () => import('@/ui-v1/views/admin/AdminCollaboratorsView.vue'),
          meta: { requiresAuth: true, requiresAdminOrOrganizer: true },
        },
        {
          path: 'challenges',
          name: 'admin-challenges',
          component: () => import('@/ui-v1/views/admin/AdminChallengesView.vue'),
          meta: { requiresAuth: true, requiresAdminOrOrganizer: true },
        },
        {
          path: 'challenges/new',
          name: 'admin-challenge-create',
          component: () => import('@/ui-v1/views/admin/AdminChallengeCreateView.vue'),
          meta: { requiresAuth: true, requiresAdminOrOrganizer: true },
        },
        {
          path: 'containers',
          name: 'admin-containers',
          component: () => import('@/ui-v1/views/admin/AdminContainersView.vue'),
          meta: { requiresAuth: true, requiresAdminOrOrganizer: true },
        },
        {
          path: 'plugins',
          name: 'admin-plugins',
          component: () => import('@/ui-v1/views/admin/AdminPluginsView.vue'),
          meta: { requiresAuth: true, requiresAdminOrOrganizer: true },
        },
        {
          path: 'theme-packs',
          name: 'admin-theme-packs',
          component: () => import('@/ui-v1/views/admin/AdminThemePacksView.vue'),
          meta: { requiresAuth: true, requiresAdminOrOrganizer: true, requiresAdmin: true },
        },
        {
          path: 'infrastructure',
          name: 'admin-infrastructure',
          component: () => import('@/ui-v1/views/admin/AdminInfrastructureView.vue'),
          meta: { requiresAuth: true, requiresAdminOrOrganizer: true, requiresAdmin: true },
        },
        {
          path: 'qqbot',
          name: 'admin-qqbot',
          component: () => import('@/ui-v1/views/admin/AdminQqBotView.vue'),
          meta: { requiresAuth: true, requiresAdminOrOrganizer: true, requiresAdmin: true },
        },
        {
          path: 'email-verification',
          name: 'admin-email-verification',
          component: () => import('@/ui-v1/views/admin/AdminEmailVerificationView.vue'),
          meta: { requiresAuth: true, requiresAdminOrOrganizer: true, requiresAdmin: true },
        },
        {
          path: 'audit-logs',
          name: 'admin-audit-logs',
          component: () => import('@/ui-v1/views/admin/AdminAuditLogsView.vue'),
          meta: { requiresAuth: true, requiresAdminOrOrganizer: true, requiresAdmin: true },
        },
        {
          path: 'health',
          name: 'admin-health',
          component: () => import('@/ui-v1/views/admin/AdminHealthView.vue'),
          meta: { requiresAuth: true, requiresAdminOrOrganizer: true },
        },
        {
          path: 'logs',
          name: 'admin-logs',
          component: () => import('@/ui-v1/views/admin/AdminLogsView.vue'),
          meta: { requiresAuth: true, requiresAdminOrOrganizer: true },
        },
      ],
    },
    {
      path: '/:pathMatch(.*)*',
      name: 'not-found',
      component: () => import('@/ui-v1/views/NotFoundView.vue'),
      meta: { requiresAuth: false },
    },
  ],
})

router.beforeEach(async (to) => {
  const auth = useAuthStore()
  if (to.meta.requiresAuth && !(await auth.ensureFreshSession())) {
    return { name: 'login', query: { redirect: to.fullPath } }
  }
  if (to.meta.requiresAuth && !auth.isAuthenticated) {
    return { name: 'login', query: { redirect: to.fullPath } }
  }
  if (to.meta.requiresAdminOrOrganizer && !['Admin', 'Organizer'].includes(auth.userRole)) {
    return { name: 'competitions' }
  }
  if (to.meta.requiresAdmin && auth.userRole !== 'Admin') {
    return { name: 'competitions' }
  }
})

export default router
