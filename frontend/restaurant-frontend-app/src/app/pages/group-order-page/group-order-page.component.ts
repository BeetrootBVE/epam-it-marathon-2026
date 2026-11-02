
import { CurrencyPipe, DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { GROUP_ORDER_PREVIEW } from '@app/shared/mocks/group-orders/group-order.preview';
import { ParticipantCardComponent } from '@app/components/group-orders/participant-card/participant-card.component';

@Component({
  selector: 'app-group-order-page',
  imports: [ParticipantCardComponent, CurrencyPipe, DatePipe],
    templateUrl: './group-order-page.component.html',
  styleUrl: './group-order-page.component.scss',
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class GroupOrderPageComponent {
  readonly participants = signal(GROUP_ORDER_PREVIEW.sessions.participants);
  readonly invite = GROUP_ORDER_PREVIEW.invite;
  readonly grandTotal = GROUP_ORDER_PREVIEW.sessions.total;
}
