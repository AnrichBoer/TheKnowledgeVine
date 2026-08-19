import { Routes } from '@angular/router';
export const routes: Routes = [
  { path: '', redirectTo: '/home', pathMatch: 'full' },
  {
    path: 'home',
    loadComponent: () => import('./pages/home/home').then((module) => module.Home),
    data: {
      title: 'Knowledge Vine Montessori | Montessori School in Boksburg',
      description:
        'A calm, child-centred Montessori learning community in Atlasville, Boksburg, nurturing independence, curiosity and confidence.',
      canonicalPath: '/home',
    },
  },
  {
    path: 'about',
    loadComponent: () => import('./pages/aboutus/aboutus').then((module) => module.Aboutus),
    data: {
      title: 'Our Story | Knowledge Vine Montessori',
      description:
        'Discover the vision, mission and prepared learning environment at Knowledge Vine Montessori in Atlasville, Boksburg.',
      canonicalPath: '/about',
    },
  },
  {
    path: 'contact',
    loadComponent: () => import('./pages/contactus/contactus').then((module) => module.Contactus),
    data: {
      title: 'Book a Visit | Knowledge Vine Montessori',
      description:
        'Contact Knowledge Vine Montessori in Atlasville, Boksburg, or arrange a visit to experience our calm, child-centred environment.',
      canonicalPath: '/contact',
    },
  },
  {
    path: 'privacy',
    loadComponent: () => import('./pages/privacy/privacy').then((module) => module.Privacy),
    data: {
      title: 'Privacy Notice | Knowledge Vine Montessori',
      description: 'How Knowledge Vine Montessori handles information submitted through this website.',
      canonicalPath: '/privacy',
    },
  },
  {
    path: '**',
    loadComponent: () => import('./pages/not-found/not-found').then((module) => module.NotFound),
    data: {
      title: 'Page Not Found | Knowledge Vine Montessori',
      description: 'The requested page could not be found.',
      canonicalPath: '/404',
      robots: 'noindex, nofollow',
    },
  },
];
