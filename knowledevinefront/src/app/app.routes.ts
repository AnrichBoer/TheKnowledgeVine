import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', redirectTo: '/home', pathMatch: 'full' },
  {
    path: 'home',
    loadComponent: () => import('./pages/home/home').then(m => m.Home),
    title: 'Home',
    data: {
      description:
        'Discover Knowledge Vine Montessori in Atlasville, Boksburg: a nurturing environment for independence, curiosity, and hands-on learning.',
    },
  },
  {
    path: 'about',
    loadComponent: () => import('./pages/aboutus/aboutus').then(m => m.Aboutus),
    title: 'About Us',
    data: {
      description:
        'Learn about Knowledge Vine Montessori, our vision, and our thoughtfully prepared environment for child-led learning in Boksburg.',
    },
  },
  {
    path: 'contact',
    loadComponent: () => import('./pages/contactus/contactus').then(m => m.Contactus),
    title: 'Contact Us',
    data: {
      description:
        'Contact Knowledge Vine Montessori in Atlasville, Boksburg. Send an enquiry or arrange a visit to our learning community.',
    },
  },
  { path: 'portal', loadChildren: () => import('./portal/portal.routes').then(m => m.portalRoutes) },
  { path: '**', redirectTo: '/home' },
];
