import { Routes } from '@angular/router';
import { portalEntryGuard, portalGuard, portalLoginGuard, teacherUnsavedGuard } from './portal-guards';

export const portalRoutes: Routes = [
  { path: '', pathMatch: 'full', canActivate: [portalEntryGuard], loadComponent: () => import('./portal-home').then(m => m.PortalHome), title: 'Portal' },
  { path: 'login', canActivate: [portalLoginGuard], loadComponent: () => import('./portal-login').then(m => m.PortalLogin), title: 'Portal Sign In' },
  { path: 'forgot-password', loadComponent: () => import('./portal-password').then(m => m.PortalPassword), data: { mode: 'recovery' }, title: 'Password Help' },
  { path: 'setup-password', loadComponent: () => import('./portal-password').then(m => m.PortalPassword), data: { mode: 'setup' }, title: 'Set Up Password' },
  { path: 'reset-password', loadComponent: () => import('./portal-password').then(m => m.PortalPassword), data: { mode: 'reset' }, title: 'Reset Password' },
  { path: 'admin', canActivate: [portalGuard], data: { role: 'Admin' }, loadComponent: () => import('./portal-admin').then(m => m.PortalAdmin), title: 'School Administration', children: [
    { path: '', pathMatch: 'full', redirectTo: 'students' },
    { path: 'accounts', data: { view: 'accounts' }, loadComponent: () => import('./portal-admin-workspace').then(m => m.PortalAdminWorkspace), title: 'School Accounts' },
    { path: 'accounts/new/teacher', data: { view: 'teacher' }, loadComponent: () => import('./portal-admin-workspace').then(m => m.PortalAdminWorkspace), title: 'Add Teacher' },
    { path: 'accounts/new/parent', data: { view: 'parent' }, loadComponent: () => import('./portal-admin-workspace').then(m => m.PortalAdminWorkspace), title: 'Add Parent' },
    { path: 'accounts/:id', data: { view: 'account' }, loadComponent: () => import('./portal-admin-workspace').then(m => m.PortalAdminWorkspace), title: 'Manage Account' },
    { path: 'students', data: { view: 'students' }, loadComponent: () => import('./portal-admin-workspace').then(m => m.PortalAdminWorkspace), title: 'School Students' },
    { path: 'students/new', data: { view: 'student' }, loadComponent: () => import('./portal-admin-workspace').then(m => m.PortalAdminWorkspace), title: 'Add Student' },
    { path: 'students/:id', data: { view: 'student' }, loadComponent: () => import('./portal-admin-workspace').then(m => m.PortalAdminWorkspace), title: 'Manage Student' },
  ] },
  { path: 'teacher', canActivate: [portalGuard], canDeactivate: [teacherUnsavedGuard], data: { role: 'Teacher' }, loadComponent: () => import('./portal-teacher').then(m => m.PortalTeacher), title: 'Teacher Daily Updates' },
  { path: 'teacher/students/:id', canActivate: [portalGuard], canDeactivate: [teacherUnsavedGuard], data: { role: 'Teacher' }, loadComponent: () => import('./portal-teacher').then(m => m.PortalTeacher), title: 'Write Daily Update' },
  { path: 'parent', canActivate: [portalGuard], data: { role: 'Parent' }, loadComponent: () => import('./portal-parent').then(m => m.PortalParent), title: 'Your Child’s Daily Updates' },
];
