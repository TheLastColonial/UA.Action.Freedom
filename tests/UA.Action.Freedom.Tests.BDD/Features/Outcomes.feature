Feature: How a load ends on a convoy
    A box delivered, accepted, seized, returned to a registered hub or undeliverable (ADR 0015), a vehicle marked
    delivered, a Ukrainian goods list accepted as a whole, and a convoy closed with a report of expected against
    actual costs and of the boxes delivered or not. The Dispatcher records all of it for the Convoy Leader until
    scoped permissions give the leader their own.

    These scenarios run against the running containers (the edge on http://localhost:8080, Keycloak on
    http://localhost:8081) and skip themselves when that stack is not up.

Background:
    Given the Freedom API exposes "/convoys"

Scenario: A delivered vehicle's goods list is accepted
    Given I am authenticated as "operator"
    And a convoy exists with an insured vehicle on its published truck list
    And a manifest reference that is not yet used
    And a category exists
    And a registered receiver exists
    And the insured vehicle has set off carrying a validated box for the registered receiver
    When I mark the insured vehicle delivered
    Then the response status is 200
    And the remembered box has the outcome "Delivered"
    When I accept the goods list of the registered receiver on the insured vehicle
    Then the response status is 200
    And the remembered box has the outcome "Accepted"
    When I accept the goods list of the registered receiver on the insured vehicle
    Then the response status is 409

Scenario: A seized box leaves the load and makes the declarations stale
    Given I am authenticated as "operator"
    And a convoy exists with an insured vehicle on its published truck list
    And a manifest reference that is not yet used
    And a category exists
    And a registered receiver exists
    And the insured vehicle has set off carrying a validated box for the registered receiver
    When I record the outcome "Seized" for the remembered box
    Then the response status is 200
    And the remembered box has the outcome "Seized"
    And the vehicle no longer carries the remembered box
    And the "Gmr" declaration of the insured vehicle reads "Stale"

Scenario: A refused box returns to a registered hub and not to anywhere else
    Given I am authenticated as "operator"
    And a convoy exists with an insured vehicle on its published truck list
    And a manifest reference that is not yet used
    And a category exists
    And a registered receiver exists
    And the insured vehicle has set off carrying a validated box for the registered receiver
    When I return the remembered box to a location that does not exist
    Then the response status is 422
    Given I am authenticated as "admin"
    And a registered hub exists
    Given I am authenticated as "operator"
    When I return the remembered box to the registered hub
    Then the response status is 200
    And the remembered box is at the registered hub
    And the vehicle no longer carries the remembered box

Scenario: An outcome cannot follow one that ended the box
    Given I am authenticated as "operator"
    And a convoy exists with an insured vehicle on its published truck list
    And a manifest reference that is not yet used
    And a category exists
    And a registered receiver exists
    And the insured vehicle has set off carrying a validated box for the registered receiver
    When I record the outcome "Seized" for the remembered box
    And I record the outcome "Delivered" for the remembered box
    Then the response status is 409

Scenario: A convoy is closed after it arrives and the report names no address
    Given I am authenticated as "operator"
    And a convoy exists with an insured vehicle on its published truck list
    And a manifest reference that is not yet used
    And a category exists
    And a registered receiver exists
    And the insured vehicle has set off carrying a validated box for the registered receiver
    When I close the convoy of the insured vehicle
    Then the response status is 409
    When I mark the insured vehicle delivered
    And I mark the manifest's convoy arrived
    Then the response status is 204
    When I close the convoy of the insured vehicle
    Then the response status is 200
    When I GET "/report" on the convoy of the insured vehicle
    Then the response status is 200
    And the report counts 1 box delivered and 0 not delivered
    And the report names no address, contact or receiver identifier

Scenario: Only a Dispatcher or an Administrator records an outcome
    Given I am authenticated as "operator"
    And a convoy exists with an insured vehicle on its published truck list
    Given I am authenticated as "loader"
    When I POST "/boxes/1/outcome" on the convoy of the insured vehicle
    Then the response status is 403
