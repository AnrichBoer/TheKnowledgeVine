import { inject, Injectable } from '@angular/core';
import { Meta, Title } from '@angular/platform-browser';
import { RouterStateSnapshot, TitleStrategy } from '@angular/router';

@Injectable()
export class SchoolTitleStrategy extends TitleStrategy {
  private readonly title = inject(Title);
  private readonly meta = inject(Meta);
  override updateTitle(snapshot: RouterStateSnapshot) {
    this.title.setTitle((this.buildTitle(snapshot) ?? 'Home') + ' | Knowledge Vine Montessori');
    let route = snapshot.root;
    while (route.firstChild) route = route.firstChild;
    this.meta.updateTag({
      name: 'description',
      content:
        route.data['description'] ??
        'A calm, nurturing Montessori learning community in Atlasville, Boksburg. Discover Knowledge Vine Montessori.',
    });
  }
}
