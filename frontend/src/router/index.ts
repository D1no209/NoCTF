import { createRouter, createWebHistory } from 'vue-router'
import { canManagePlatformResources, isPlatformAdministrator } from '@/api/userRole'
import { useAuthStore } from '@/stores/auth'

const router = createRouter({
  history: createWebHistory(),
  scrollBehavior: (_to, _from, savedPosition) => savedPosition ?? { left: 0, top: 0 },
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
      path: '/verify-email',
      name: 'verify-email',
      component: () => import('@/views/VerifyEmailView.vue'),
      meta: { requiresAuth: false },
    },
    {
      path: '/forgot-password',
      name: 'forgot-password',
      component: () => import('@/views/ForgotPasswordView.vue'),
      meta: { requiresAuth: false },
    },
    {
      path: '/reset-password',
      name: 'reset-password',
      component: () => import('@/views/ResetPasswordView.vue'),
      meta: { requiresAuth: false },
    },
    {
      path: '/competitions',
      name: 'competitions',
      component: () => import('@/views/CompetitionsView.vue'),
      meta: { requiresAuth: true },
    },
    {
      path: '/profile',
      name: 'profile',
      component: () => import('@/views/ProfileView.vue'),
      meta: { requiresAuth: true },
    },
    {
      path: '/teams',
      name: 'my-teams',
      component: () => import('@/views/MyTeamsView.vue'),
      meta: { requiresAuth: true },
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
          path: 'email-verification',
          redirect: { name: 'admin-email-verification' },
          meta: { requiresAuth: true, requiresAdminOrOrganizer: true, requiresAdmin: true },
        },
        {
          path: 'settings',
          component: () => import('@/views/admin/AdminSettingsLayout.vue'),
          meta: { requiresAuth: true, requiresAdminOrOrganizer: true, requiresAdmin: true },
          children: [
            {
              path: '',
              redirect: { name: 'admin-platform-basic' },
            },
            {
              path: 'basic',
              name: 'admin-platform-basic',
              component: () => import('@/views/admin/AdminPlatformBasicView.vue'),
            },
            {
              path: 'email-verification',
              name: 'admin-email-verification',
              component: () => import('@/views/admin/AdminEmailVerificationView.vue'),
            },
            {
              path: 'information',
              name: 'admin-platform-information',
              component: () => import('@/views/admin/AdminPlatformInformationView.vue'),
            },
          ],
        },
        {
          path: 'competitions',
          name: 'admin-competitions',
          component: () => import('@/views/admin/AdminCompetitionsView.vue'),
          meta: { requiresAuth: true, requiresAdminOrOrganizer: true },
        },
        {
          path: 'competitions/:id/challenges/new',
          name: 'admin-competition-challenge-create',
          component: () => import('@/views/admin/AdminCompetitionChallengeEditorView.vue'),
          meta: { requiresAuth: true, requiresAdminOrOrganizer: true },
        },
        {
          path: 'competitions/:id/challenges/:competitionChallengeId/edit',
          name: 'admin-competition-challenge-edit',
          component: () => import('@/views/admin/AdminCompetitionChallengeEditorView.vue'),
          meta: { requiresAuth: true, requiresAdminOrOrganizer: true },
        },
        {
          path: 'competitions/:id',
          name: 'admin-competition-detail',
          component: () => import('@/views/admin/AdminCompetitionDetailView.vue'),
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
          path: 'health',
          name: 'admin-health',
          component: () => import('@/views/admin/AdminHealthView.vue'),
          meta: { requiresAuth: true, requiresAdminOrOrganizer: true },
        },
        {
          path: 'platform-logs',
          name: 'admin-platform-logs',
          component: () => import('@/views/admin/AdminPlatformLogsView.vue'),
          meta: { requiresAuth: true, requiresAdminOrOrganizer: true, requiresAdmin: true },
        },
      ],
    },
    {
      path: '/:pathMatch(.*)*',
      name: 'not-found',
      component: () => import('@/views/NotFoundView.vue'),
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
  if (
    auth.isAuthenticated
    && auth.emailVerified === false
    && to.name !== 'verify-email'
    && to.name !== 'login'
    && to.name !== 'forgot-password'
    && to.name !== 'reset-password'
  ) {
    return { name: 'verify-email' }
  }
  if (to.meta.requiresAdminOrOrganizer && !canManagePlatformResources(auth.userRole)) {
    return { name: 'competitions' }
  }
  if (to.meta.requiresAdmin && !isPlatformAdministrator(auth.userRole)) {
    return { name: 'competitions' }
  }
})

export default router
