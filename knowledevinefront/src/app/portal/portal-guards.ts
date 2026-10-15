import { inject } from '@angular/core';
import { CanActivateFn, CanDeactivateFn, Router } from '@angular/router';
import type { PortalTeacher } from './portal-teacher';
import { PortalAuth, PortalRole } from './portal-auth';

export const teacherUnsavedGuard: CanDeactivateFn<PortalTeacher> = component => component.mayLeave();

export const portalGuard: CanActivateFn = async route => {
  const auth = inject(PortalAuth);
  const router = inject(Router);
  try {
    const session = await auth.loadSession();
    if (!session) return router.parseUrl('/portal/login');
    const role = route.data['role'] as PortalRole | undefined;
    if (role && !session.roles.includes(role)) return router.parseUrl(auth.destination(session));
    if (role) await auth.access(role);
    return true;
  } catch {
    return router.parseUrl('/portal/login?unavailable=1');
  }
};

export const portalEntryGuard: CanActivateFn = async () => {
  const auth = inject(PortalAuth);
  const router = inject(Router);
  try { return router.parseUrl(auth.destination(await auth.loadSession())); }
  catch { return router.parseUrl('/portal/login?unavailable=1'); }
};

export const portalLoginGuard: CanActivateFn = async () => {
  const auth = inject(PortalAuth);
  const router = inject(Router);
  try {
    const session = await auth.loadSession();
    return session ? router.parseUrl(auth.destination(session)) : true;
  } catch { return true; }
};
