Feature: Manifests API
    The deployed Freedom service exposes manifests at /manifests: the Administrator's sign-off of
    one vehicle's load on one convoy, and its document pack (border weight, delivery notes). Its
    cargo and its ferry booking are recorded against the same truck-list entry, at
    /convoys/{id}/vehicles/{vin}/boxes and /ferry, and the manifest reads them.

    A manifest is opened against a truck-list entry, at
    POST /convoys/{id}/vehicles/{vin}/manifest, because that pair is what it is the paperwork
    for. It opens Proposed. It carries no crew of its own: who is driving is a fact about the
    vehicle on the convoy, and /manifests/{id}/crew reads it.

    The states are Proposed, Approved and Rejected (docs/manifest-status.puml). Each edge a person
    can take is its own POST: propose, approve and reject. A rejected manifest may only be proposed
    against a convoy whose truck list is published. Nothing is frozen: a change to the vehicle's
    load (a box put on, moved, taken off, replaced, or lost) sends an approved manifest back to
    Proposed, and it needs approving again. What happened to a load is a box outcome, not a
    manifest state.

    Approval is the Administrator's sign-off of the load, and files nothing. It is Administrator
    only, so the person who builds a manifest is not the person who signs it off.

    These scenarios run against the running containers (the edge on
    http://localhost:8080, Keycloak on http://localhost:8081) and skip themselves
    when that stack is not up.

Background:
    Given the Freedom API exposes "/manifests"

Scenario: Reading manifests requires a token
    When I GET "/manifests" without a token
    Then the response status is 401

Scenario: A ground officer is refused manifests
    Given I am authenticated as "groundofficer"
    When I GET "/manifests"
    Then the response status is 403

Scenario: A dispatcher opens a manifest in the Proposed state
    Given I am authenticated as "operator"
    And a convoy exists whose truck list is not published
    And a manifest reference that is not yet used
    When I POST a manifest on the remembered convoy
    Then the response status is 201
    When I GET the remembered manifest
    Then the response status is 200
    And the response body field "status" is "Proposed"

Scenario: Reusing a manifest reference is a conflict
    Given I am authenticated as "operator"
    And a convoy exists whose truck list is not published
    And a manifest reference that is not yet used
    When I POST a manifest on the remembered convoy
    Then the response status is 201
    When I POST a manifest on the remembered convoy
    Then the response status is 409

Scenario: A manifest cannot be opened for a vehicle that is not on the convoy
    Given I am authenticated as "operator"
    And a convoy exists whose truck list is not published
    And a manifest reference that is not yet used
    When I POST a manifest for a vehicle that is not on the convoy
    Then the response status is 404

Scenario: A rejected manifest cannot be proposed until its convoy has published a truck list
    Given I am authenticated as "operator"
    And a convoy exists whose truck list is not published
    And a manifest reference that is not yet used
    When I POST a manifest on the remembered convoy
    Then the response status is 201
    When I POST "reject" on the remembered manifest
    Then the response status is 204
    When I POST "propose" on the remembered manifest
    Then the response status is 409
    When I GET the remembered manifest
    Then the response body field "status" is "Rejected"

Scenario: A rejected manifest is proposed once its convoy has published a truck list
    Given I am authenticated as "operator"
    And a convoy exists whose truck list is published
    And a manifest reference that is not yet used
    When I POST a manifest on the remembered convoy
    Then the response status is 201
    When I POST "reject" on the remembered manifest
    Then the response status is 204
    When I POST "propose" on the remembered manifest
    Then the response status is 204
    When I GET the remembered manifest
    Then the response body field "status" is "Proposed"

Scenario: A dispatcher may build a manifest but not approve it
    Given I am authenticated as "operator"
    And a convoy exists whose truck list is published
    And a manifest reference that is not yet used
    When I POST a manifest on the remembered convoy
    Then the response status is 201
    When I POST "approve" on the remembered manifest
    Then the response status is 403

Scenario: An administrator approves a manifest, which signs the load off
    Given I am authenticated as "operator"
    And a convoy exists whose truck list is published
    And a manifest reference that is not yet used
    When I POST a manifest on the remembered convoy
    Then the response status is 201
    Given I am authenticated as "admin"
    When I POST "approve" on the remembered manifest
    Then the response status is 204
    When I GET the remembered manifest
    Then the response body field "status" is "Approved"
    And the response body field "approved" is "True"

Scenario: An approved manifest still takes its notes and can be deleted
    Given I am authenticated as "operator"
    And a convoy exists whose truck list is published
    And a manifest reference that is not yet used
    When I POST a manifest on the remembered convoy
    Then the response status is 201
    Given I am authenticated as "admin"
    When I POST "approve" on the remembered manifest
    Then the response status is 204
    When I PUT the remembered manifest with body:
        """
        { "deliveryNotes": "changed after sign-off" }
        """
    Then the response status is 204
    When I DELETE the remembered manifest
    Then the response status is 204

Scenario: A change to the load sends an approved manifest back, and it is approved again
    Given I am authenticated as "operator"
    And a convoy exists with an insured vehicle on its published truck list
    And a registered receiver exists
    And a manifest reference that is not yet used
    When I POST "/boxes" with body:
        """
        { "receiverRef": "{receiver}" }
        """
    Then the response status is 201
    Given I remember the box
    When I POST a manifest for the insured vehicle on the remembered convoy
    Then the response status is 201
    Given I am authenticated as "admin"
    When I POST "approve" on the remembered manifest
    Then the response status is 204
    When I GET the remembered manifest
    Then the response body field "status" is "Approved"
    When I put the remembered box on the insured vehicle
    Then the response status is 204
    When I GET the remembered manifest
    Then the response body field "status" is "Proposed"
    And the response body field "approved" is "False"
    When I POST "approve" on the remembered manifest
    Then the response status is 204
    When I GET the remembered manifest
    Then the response body field "status" is "Approved"

Scenario: The delivery verbs are gone because what happened to a load is a box outcome
    Given I am authenticated as "operator"
    And a convoy exists whose truck list is published
    And a manifest reference that is not yet used
    When I POST a manifest on the remembered convoy
    Then the response status is 201
    When I POST "deliver" on the remembered manifest
    Then the response status is 404
    When I POST "prepare" on the remembered manifest
    Then the response status is 404

Scenario: A rejected manifest can be proposed again
    Given I am authenticated as "operator"
    And a convoy exists whose truck list is published
    And a manifest reference that is not yet used
    When I POST a manifest on the remembered convoy
    Then the response status is 201
    When I POST "reject" on the remembered manifest
    Then the response status is 204
    When I POST "propose" on the remembered manifest
    Then the response status is 204
    When I GET the remembered manifest
    Then the response body field "status" is "Proposed"

Scenario: The border weight shows its fixed allowances
    Given I am authenticated as "operator"
    And a convoy exists whose truck list is not published
    And a manifest reference that is not yet used
    When I POST a manifest on the remembered convoy
    Then the response status is 201
    When I GET "/weight" on the remembered manifest
    Then the response status is 200
    And the response body field "crewAndBagsKg" is "200"
    And the response body field "fuelKg" is "45"
    And the response body field "unvalidatedBoxCount" is "0"

Scenario: A vehicle's outbound ferry is booked on its truck-list entry
    Given I am authenticated as "operator"
    And a convoy exists with an insured vehicle on its published truck list
    When I book a ferry for the insured vehicle
    Then the response status is 204
    When I GET the ferry booking of the insured vehicle
    Then the response status is 200
    And the response body field "reference" is "BDD-FERRY-1"
    And the response body field "operator" is "P&O Ferries"

Scenario: Cargo is written on the vehicle, and the manifest has no route for it
    Given I am authenticated as "operator"
    And a convoy exists with an insured vehicle on its published truck list
    And a manifest reference that is not yet used
    When I POST a manifest for the insured vehicle on the remembered convoy
    Then the response status is 201
    When I PUT "/boxes/1" on the remembered manifest
    Then the response status is 404

Scenario: Fetching an unknown manifest is a 404
    Given I am authenticated as "operator"
    When I GET "/manifests/NOSUCHMANIFEST"
    Then the response status is 404

Scenario: A convoy arrives once its vehicles are delivered, and the vehicle is handed over for good
    Given I am authenticated as "operator"
    And a convoy exists with an insured vehicle on its published truck list
    And a manifest reference that is not yet used
    When I POST a manifest for the insured vehicle on the remembered convoy
    Given I am authenticated as "admin"
    When I POST "approve" on the remembered manifest
    And I make the convoy of the insured vehicle ready to depart
    And I depart the convoy of the insured vehicle
    Then the response status is 200
    When I mark the manifest's convoy arrived
    Then the response status is 409
    When I mark the insured vehicle delivered
    And I mark the manifest's convoy arrived
    Then the response status is 204
    And the insured vehicle has been handed over
    And the insured vehicle cannot join another convoy

Scenario: A driver is erased only once their convoy has arrived, and the history keeps an anonymous seat
    Given I am authenticated as "operator"
    And a convoy exists with an insured vehicle on its published truck list
    And a manifest reference that is not yet used
    When I POST a manifest for the insured vehicle on the remembered convoy
    Given I am authenticated as "admin"
    When I POST "approve" on the remembered manifest
    And I make the convoy of the insured vehicle ready to depart
    And I depart the convoy of the insured vehicle
    And the administrator erases the vehicle's driver
    Then the response status is 409
    When I mark the insured vehicle delivered
    And I mark the manifest's convoy arrived
    And the administrator erases the vehicle's driver
    Then the response status is 204
    And the vehicle's crew on that convoy shows a former volunteer
