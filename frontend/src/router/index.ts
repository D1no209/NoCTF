import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: '/',
      redirect: '/competitions',
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
      path: '/competitions/:id',
      name: 'competition-detail',
      component: () => import('@/views/CompetitionGatewayView.vue'),
      meta: { requiresAuth: true },
    },
    {
      path: '/competitions/:id/awd',
      name: 'awd-dashboard',
      component: () => import('@/views/AwdDashboardView.vue'),
      meta: { requiresAuth: true },
    },
    {
      path: '/competitions/:id/koh',
      name: 'koh-dashboard',
      component: () => import('@/views/KohDashboardView.vue'),
      meta: { requiresAuth: true },
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
  if (to.meta.requiresAuth && !auth.isAuthenticated) {
    return { name: 'login' }
  }
  if (to.meta.requiresAdminOrOrganizer && !['Admin', 'Organizer'].includes(auth.userRole)) {
    return { name: 'competitions' }
  }
  if (to.meta.requiresAdmin && auth.userRole !== 'Admin') {
    return { name: 'competitions' }
  }
})

export default router
