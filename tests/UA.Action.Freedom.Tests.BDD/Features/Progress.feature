Feature: Progress on the road
    The Convoy Leader reports a convoy's progress point by point: a route point reached, a border crossed (ADR 0016).
    Nothing is tracked and no position is kept, only that somebody said so and when. A Dispatcher may enter a mark
    on the leader's behalf and the record names the Dispatcher. Crossing a border closes the vehicle's declarations
    for that border, and every answer the checklist page fetches is sent no-store so nothing is kept on the phone.

    The "leader" seed login carries no application role at all. It becomes the Convoy Leader when a Dispatcher
    nominates the volunteer it is linked to.

    These scenarios run against the running containers (the edge on http://localhost:8080, Keycloak on
    http://localhost:8081) and skip themselves when that stack is not up.

Background:
    Given the Freedom API exposes "/convoys"

Scenario: The leader marks a stop reached and HQ sees it
    Given I am authenticated as "operator"
    And a convoy exists with an insured vehicle on its published truck list
    And a manifest reference that is not yet used
    And a category exists
    And a registered receiver exists
    And the insured vehicle has set off carrying a validated box for the registered receiver
    And the "leader" login's volunteer leads the convoy of the insured vehicle
    Given I am authenticated as "leader"
    When I mark the route point "Coventry depot" of the convoy of the insured vehicle reached
    Then the response status is 204
    And the response is sent no-store
    Given I am authenticated as "operator"
    Then the progress of the convoy of the insured vehicle shows "Coventry depot" as "Reached" by the leader

Scenario: The Dispatcher marks a crossing on the leader's behalf and is named, and the border's declarations close
    Given I am authenticated as "operator"
    And a convoy exists with an insured vehicle on its published truck list
    And a manifest reference that is not yet used
    And a category exists
    And a registered receiver exists
    And the insured vehicle has set off carrying a validated box for the registered receiver
    And the route of the convoy of the insured vehicle has a border into "EU"
    Then the "Ens" declaration of the insured vehicle reads "Accepted"
    When I mark the route point "BDD border" of the convoy of the insured vehicle crossed
    Then the response status is 204
    And the progress of the convoy of the insured vehicle shows "BDD border" as "Crossed" on the leader's behalf
    And the "Ens" declaration of the insured vehicle reads "Closed"
    And the "Gmr" declaration of the insured vehicle reads "Filed"

Scenario: Only a border can be crossed
    Given I am authenticated as "operator"
    And a convoy exists with an insured vehicle on its published truck list
    And a manifest reference that is not yet used
    And a category exists
    And a registered receiver exists
    And the insured vehicle has set off carrying a validated box for the registered receiver
    When I mark the route point "Coventry depot" of the convoy of the insured vehicle crossed
    Then the response status is 422

Scenario: The leader of another convoy is refused
    Given I am authenticated as "operator"
    And a convoy exists with an insured vehicle on its published truck list
    And a manifest reference that is not yet used
    And a category exists
    And a registered receiver exists
    And the insured vehicle has set off carrying a validated box for the registered receiver
    And the "leader" login's volunteer leads the convoy of the insured vehicle
    And another convoy exists
    Given I am authenticated as "leader"
    When I mark route point 1 of the other convoy reached
    Then the response status is 403
    And the response is sent no-store

Scenario: The leader enters fuel and nothing else
    Given I am authenticated as "operator"
    And a convoy exists with an insured vehicle on its published truck list
    And a manifest reference that is not yet used
    And a category exists
    And a registered receiver exists
    And the insured vehicle has set off carrying a validated box for the registered receiver
    And the "leader" login's volunteer leads the convoy of the insured vehicle
    Given I am authenticated as "leader"
    When I add a "Fuel" cost of 40 to the convoy of the insured vehicle
    Then the response status is 201
    When I add a "Other" cost of 40 to the convoy of the insured vehicle
    Then the response status is 403
