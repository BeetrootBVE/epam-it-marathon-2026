import { Location } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { API_BASE_URL } from '@app/shared/config/api-base-url.token';
import { Observable } from 'rxjs';

@Injectable({
  providedIn: 'root',
})
export class FeedbackQrService {
  private readonly location = inject(Location);
  private readonly http: HttpClient = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  generateFeedbackUrl(
    token: string,
    reservationId: string,
    waiterName: string,
    isNewFeedback: boolean
  ): string {
    const params = new URLSearchParams({
      token,
      reservationId,
      waiterName,
      isNewFeedback: String(isNewFeedback),
    });
    const path = this.location.prepareExternalUrl('/main?' + params.toString());
    return new URL(path, document.baseURI).href;
  }

  getAnonymousFeedbackToken(reservationId: string): Observable<string> {
    const url = this.baseUrl + '/feedback/token';
    const params = new URLSearchParams({
      reservationId: reservationId,
    });
    return this.http.get<string>(`${url}?${params.toString()}`);
  }
}
