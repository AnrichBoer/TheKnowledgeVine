import { Routes } from '@angular/router';
import { Home } from './pages/home/home';
import { Contactus } from './pages/contactus/contactus';
import { Aboutus } from './pages/aboutus/aboutus';

export const routes: Routes = [

    {path: '', redirectTo: '/home', pathMatch: 'full'},
    {path: 'home', component: Home},
    {path: 'contact', component: Contactus},
    {path: 'about', component: Aboutus},

];
