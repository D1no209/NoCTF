import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: '/',
      name: 'home',
      component: () => import('@/views/HomeView.vue'),
      meta: { requiresAuth: true },
    },
    {
      path: '/login',
      name: 'login',
      component: () => import('@/views/LoginView.vue'),
      meta: { requiresAuth: false },
    },
    {
      path: '/register',
      name: 'register',
      component: () => import('@/views/RegisterView.vue'),
      meta: { requiresAuth: false },
    },
    {
      path: '/competitions',
      name: 'competitions',
      component: () => import('@/views/CompetitionsView.vue'),
      meta: { requiresAuth: true },
    },
    {
      path: '/teams',
      name: 'teams',
      component: () => import('@/views/TeamsView.vue'),
      meta: { requiresAuth: true },
    },
    {
      path: '/awdp/screen/:gameId',
      name: 'awdp-screen',
      component: () => import('@/views/AwdpScreenView.vue'),
      meta: { requiresAuth: false },
    },
    {
      path: '/competitions/:id/register',
      name: 'competition-register',
      component: () => import('@/views/CompetitionRegistrationView.vue'),
      meta: { requiresAuth: true },
    },
    {
      path: '/competitions/:id',
      component: () => import('@/components/layout/GameLayout.vue'),
      meta: { requiresAuth: true },
      children: [
        {
          path: '',
          name: 'competition-detail',
          component: () => import('@/views/CompetitionGatewayView.vue'),
        },
        {
          path: 'awd',
          name: 'awd-dashboard',
          component: () => import('@/views/AwdDashboardView.vue'),
        },
        {
          path: 'koh',
          name: 'koh-dashboard',
          component: () => import('@/views/KohDashboardView.vue'),
        },
      ],
    },
    {
      path: '/admin',
      component: () => import('@/views/admin/AdminLayout.vue'),
      meta: { requiresAuth: true, requiresAdminOrOrganizer: true },
      children: [
        {
          path: '',
          redirect: '/admin/competitions',
        },
        {
          path: 'users',
          name: 'admin-users',
          component: () => import('@/views/admin/AdminUsersView.vue'),
          meta: { requiresAuth: true, requiresAdminOrOrganizer: true, requiresAdmin: true },
        },
        {
          path: 'teams',
          name: 'admin-teams',
          component: () => import('@/views/admin/AdminTeamsView.vue'),
          meta: { requiresAuth: true, requiresAdminOrOrganizer: true },
        },
        {
          path: 'competitions',
          name: 'admin-competitions',
          component: () => import('@/views/admin/AdminCompetitionsView.vue'),
          meta: { requiresAuth: true, requiresAdminOrOrganizer: true },
        },
        {
          path: 'competitions/:id',
          name: 'admin-competition-detail',
          component: () => import('@/views/admin/AdminCompetitionDetailView.vue'),
          meta: { requiresAuth: true, requiresAdminOrOrganizer: true },
        },
        {
          path: 'collaborators',
          name: 'admin-collaborators',
          component: () => import('@/views/admin/AdminCollaboratorsView.vue'),
          meta: { requiresAuth: true, requiresAdminOrOrganizer: true },
        },
        {
          path: 'challenges',
          name: 'admin-challenges',
          component: () => import('@/views/admin/AdminChallengesView.vue'),
          meta: { requiresAuth: true, requiresAdminOrOrganizer: true },
        },
        {
          path: 'challenges/new',
          name: 'admin-challenge-create',
          component: () => import('@/views/admin/AdminChallengeCreateView.vue'),
          meta: { requiresAuth: true, requiresAdminOrOrganizer: true },
        },
        {
          path: 'containers',
          name: 'admin-containers',
          component: () => import('@/views/admin/AdminContainersView.vue'),
          meta: { requiresAuth: true, requiresAdminOrOrganizer: true },
        },
        {
          path: 'plugins',
          name: 'admin-plugins',
          component: () => import('@/views/admin/AdminPluginsView.vue'),
          meta: { requiresAuth: true, requiresAdminOrOrganizer: true },
        },
        {
          path: 'audit-logs',
          name: 'admin-audit-logs',
          component: () => import('@/views/admin/AdminAuditLogsView.vue'),
          meta: { requiresAuth: true, requiresAdminOrOrganizer: true, requiresAdmin: true },
        },
        {
          path: 'health',
          name: 'admin-health',
          component: () => import('@/views/admin/AdminHealthView.vue'),
          meta: { requiresAuth: true, requiresAdminOrOrganizer: true },
        },
        {
          path: 'logs',
          name: 'admin-logs',
          component: () => import('@/views/admin/AdminLogsView.vue'),
          meta: { requiresAuth: true, requiresAdminOrOrganizer: true },
        },
      ],
    },
  ],
})

router.beforeEach((to) => {
  const auth = useAuthStore()
  if (to.meta.requiresAuth && !auth.ensureFreshSession()) {
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
