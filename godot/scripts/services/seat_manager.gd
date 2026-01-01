extends Node
class_name SeatManager

var seats: Array = [] ## array of seat identifiers or nodes
var occupied := {}

func get_free_seats() -> Array:
    var free := []
    for seat in seats:
        if not occupied.has(seat):
            free.append(seat)
    return free

func claim_seat(seat, npc) -> void:
    occupied[seat] = npc

func release_seat(seat) -> void:
    occupied.erase(seat)
