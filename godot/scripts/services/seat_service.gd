extends Node
class_name SeatService

var seat_manager: SeatManager

## Claims a random free seat for the given npc. Returns the seat identifier or null.
func claim_free_seat(npc) -> Variant:
    if seat_manager == null:
        return null
    var free_seats := seat_manager.get_free_seats()
    free_seats.shuffle()
    if free_seats.is_empty():
        return null
    var chosen := free_seats.front()
    seat_manager.claim_seat(chosen, npc)
    return chosen
