import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

// Explica o caminho inválido e oferece retorno para a única página disponível.
@Component({
  imports: [RouterLink],
  selector: 'app-not-found',
  templateUrl: './not-found.html',
})
export class NotFoundPage {}
